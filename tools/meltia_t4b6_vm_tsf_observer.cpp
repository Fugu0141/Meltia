// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// T4-B6: VM-ONLY TSF observer producing T4-B3 evidence. No registration functions.
// GetDisplayAttributeInfo may activate an existing third-party COM provider:
// --vm-readonly is intentionally opt-in and MUST NOT run on CI/host machines.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <msctf.h>
#include <cwchar>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>
#include <utility>

#include "MeltiaT4b6IdentitySnapshot.h"

namespace {

struct AttributeResult {
    bool resolved = false;
    GUID owner{};
};

struct CategoryResult {
    bool complete = false;
    bool present = false;
};

constexpr unsigned long kMaxItems = 100000;
constexpr const wchar_t* kFields[3] = {
    L"inputAttributeGuid", L"convertedAttributeGuid", L"targetAttributeGuid"
};
constexpr const wchar_t* kCategoryNames[5] = {
    L"GUID_TFCAT_TIP_KEYBOARD",
    L"GUID_TFCAT_DISPLAYATTRIBUTEPROVIDER",
    L"GUID_TFCAT_TIPCAP_INPUTMODECOMPARTMENT",
    L"GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT",
    L"GUID_TFCAT_TIPCAP_SYSTRAYSUPPORT"
};
const GUID* const kCategoryGuids[5] = {
    &GUID_TFCAT_TIP_KEYBOARD,
    &GUID_TFCAT_DISPLAYATTRIBUTEPROVIDER,
    &GUID_TFCAT_TIPCAP_INPUTMODECOMPARTMENT,
    &GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT,
    &GUID_TFCAT_TIPCAP_SYSTRAYSUPPORT
};

std::wstring GuidText(const GUID& value) {
    wchar_t text[40]{};
    return StringFromGUID2(value, text, 40) ? std::wstring(text) : std::wstring();
}

bool ParseGuid(const wchar_t* text, GUID& value) {
    return text != nullptr && SUCCEEDED(CLSIDFromString(text, &value));
}


// The build generates this snapshot ONLY after checking the entire T2
// design-only manifest (including all deployment flags). At runtime, before
// CoInitializeEx, require BOTH identical manifest bytes and four matching,
// correctly ordered GUIDs. Merely well-formed random GUIDs are never allowed.
bool ManifestMatches(const std::string& bytes) {
    constexpr size_t kLength = sizeof(meltia_t4b6_identity::kManifestBytes) - 1;
    return bytes.size() == kLength &&
           bytes.compare(0, kLength, meltia_t4b6_identity::kManifestBytes, kLength) == 0;
}

bool GuidRolesMatch(const GUID (&ids)[4]) {
    for (int i = 0; i < 4; ++i) {
        GUID expected{};
        if (!ParseGuid(meltia_t4b6_identity::kExpectedGuids[i], expected) ||
            !IsEqualGUID(ids[i], expected)) return false;
    }
    return true;
}

bool ReadAndMatchManifest(const wchar_t* path) {
    if (!path || !*path) return false;
    std::ifstream input(std::filesystem::path(path), std::ios::binary | std::ios::ate);
    if (!input) return false;
    constexpr size_t kLength = sizeof(meltia_t4b6_identity::kManifestBytes) - 1;
    if (input.tellg() != static_cast<std::streamoff>(kLength)) return false;
    input.seekg(0, std::ios::beg);
    std::string bytes(kLength, '\0');
    if (!input.read(&bytes[0], static_cast<std::streamsize>(bytes.size()))) return false;
    return ManifestMatches(bytes);
}

bool ValidateBoundT2Inputs(const wchar_t* manifestPath, wchar_t* const* args,
                           GUID (&ids)[4]) {
    for (int i = 0; i < 4; ++i)
        if (!ParseGuid(args[i], ids[i])) return false;
    return ReadAndMatchManifest(manifestPath) && GuidRolesMatch(ids);
}

bool ApplyCategoryPage(CategoryResult& result, HRESULT hr, ULONG fetched,
                       const GUID* item, const GUID& target) {
    if (hr == S_FALSE && fetched == 0) {
        result.complete = true;
        return false;
    }
    if (hr != S_OK || fetched != 1 || !item) {
        // Even if an occupied item was observed before the failure, no
        // partial count is promoted to an authoritative complete result.
        result.complete = false;
        result.present = false;
        return false;
    }
    if (IsEqualGUID(*item, target)) result.present = true;
    return true;
}

AttributeResult ClassifyAttribute(HRESULT hr, bool infoAvailable, const GUID& owner) {
    AttributeResult result;
    if (hr == S_OK && infoAvailable) {
        result.resolved = true;
        result.owner = owner;
    }
    return result;
}

CategoryResult ObserveCategory(ITfCategoryMgr* manager, const GUID& category, const GUID& clsid) {
    CategoryResult result;
    if (!manager) return result;
    IEnumGUID* enumerator = nullptr;
    const HRESULT start = manager->EnumItemsInCategory(category, &enumerator);
    if (FAILED(start) || !enumerator) {
        if (enumerator) enumerator->Release();
        return result;
    }
    for (unsigned long n = 0; n < kMaxItems; ++n) {
        GUID item{};
        ULONG fetched = 0;
        HRESULT hr = enumerator->Next(1, &item, &fetched);
        if (!ApplyCategoryPage(result, hr, fetched, &item, clsid)) break;
    }
    enumerator->Release();
    // A partial enumeration cannot claim that the target is or is not present.
    if (!result.complete) result.present = false;
    return result;
}

AttributeResult ObserveAttribute(ITfDisplayAttributeMgr* manager, const GUID& guid) {
    AttributeResult result;
    if (!manager) return result;
    ITfDisplayAttributeInfo* info = nullptr;
    GUID owner{};
    HRESULT hr = manager->GetDisplayAttributeInfo(guid, &info, &owner);
    result = ClassifyAttribute(hr, info != nullptr, owner);
    if (info) info->Release();
    // No HRESULT other than successful resolution proves global absence.
    return result;
}

void PrintEvidence(std::wostream& out, const GUID& clsid, const GUID attrs[3],
                   const AttributeResult values[3], const CategoryResult categories[5]) {
    const std::wstring clsidText = GuidText(clsid);
    out << L"{\"phase\":\"T4-B3\",\"coverage\":\"current-user-visible-tsf-only\","
        << L"\"registry_writes\":false,\"registration_authorized\":false,\"attributes\":[";
    for (int i = 0; i < 3; ++i) {
        if (i) out << L",";
        out << L"{\"field\":\"" << kFields[i] << L"\",\"guid\":\"" << GuidText(attrs[i])
            << L"\",\"lookup\":\"" << (values[i].resolved ? L"resolved" : L"failed")
            << L"\",\"owner_clsid\":";
        if (values[i].resolved) out << L"\"" << GuidText(values[i].owner) << L"\"";
        else out << L"null";
        out << L"}";
    }
    out << L"],\"categories\":[";
    for (int i = 0; i < 5; ++i) {
        if (i) out << L",";
        out << L"{\"category\":\"" << kCategoryNames[i] << L"\",\"item_clsid\":\""
            << clsidText << L"\",\"complete\":" << (categories[i].complete ? L"true" : L"false")
            << L",\"present\":";
        if (categories[i].complete) out << (categories[i].present ? L"true" : L"false");
        else out << L"null";
        out << L"}";
    }
    out << L"]}\n";
}

bool SelfTest() {
    GUID clsid{}, attrs[3]{}, thirdParty{};
    if (!ParseGuid(L"{00000001-0000-4000-8000-000000000001}", clsid) ||
        !ParseGuid(L"{00000002-0000-4000-8000-000000000002}", attrs[0]) ||
        !ParseGuid(L"{00000003-0000-4000-8000-000000000003}", attrs[1]) ||
        !ParseGuid(L"{00000004-0000-4000-8000-000000000004}", attrs[2]) ||
        !ParseGuid(L"{00000005-0000-4000-8000-000000000005}", thirdParty)) return false;
    AttributeResult attributes[3]{};
    CategoryResult categories[5]{};
    std::wostringstream unknown;
    PrintEvidence(unknown, clsid, attrs, attributes, categories);
    const std::wstring a = unknown.str();
    if (a.find(L"\"lookup\":\"failed\",\"owner_clsid\":null") == std::wstring::npos ||
        a.find(L"\"complete\":false,\"present\":null") == std::wstring::npos ||
        a.find(L"\"registration_authorized\":false") == std::wstring::npos) return false;
    attributes[0].resolved = true;
    attributes[0].owner = thirdParty;
    categories[1].complete = true;
    categories[1].present = true;
    categories[2].complete = true;
    std::wostringstream occupied;
    PrintEvidence(occupied, clsid, attrs, attributes, categories);
    const std::wstring b = occupied.str();
    if (b.find(L"\"lookup\":\"resolved\",\"owner_clsid\":\"{00000005-0000-4000-8000-000000000005}\"")
            == std::wstring::npos ||
        b.find(L"\"complete\":true,\"present\":true") == std::wstring::npos ||
        b.find(L"\"complete\":true,\"present\":false") == std::wstring::npos) return false;

    // Exercise precisely the same guard called before live CoInitializeEx.
    GUID allowed[4]{}, swapped[4]{}, arbitrary[4]{};
    for (int i = 0; i < 4; ++i) {
        if (!ParseGuid(meltia_t4b6_identity::kExpectedGuids[i], allowed[i])) return false;
        swapped[i] = allowed[i];
        arbitrary[i] = allowed[i];
    }
    if (!GuidRolesMatch(allowed)) return false;
    const std::string raw(meltia_t4b6_identity::kManifestBytes,
                          sizeof(meltia_t4b6_identity::kManifestBytes) - 1);
    if (!ManifestMatches(raw) || ManifestMatches(raw + " ")) return false;
    std::string changed = raw;
    auto field = changed.find("\"registrationEnabled\": false");
    if (field == std::string::npos) return false;
    changed.replace(field, sizeof("\"registrationEnabled\": false") - 1,
                    "\"registrationEnabled\": true ");
    if (ManifestMatches(changed)) return false;
    std::swap(swapped[1], swapped[2]);
    if (GuidRolesMatch(swapped)) return false;
    if (!ParseGuid(L"{00000006-0000-4000-8000-000000000006}", arbitrary[2]) ||
        GuidRolesMatch(arbitrary)) return false;
    GUID duplicate[4] = {allowed[0], allowed[1], allowed[1], allowed[3]};
    if (GuidRolesMatch(duplicate)) return false;

    // Pure category step reducer: EOF, COM failure, partial occupied entry,
    // and hitting the bound without terminal EOF must never claim completeness.
    CategoryResult category{};
    if (!ApplyCategoryPage(category, S_OK, 1, &thirdParty, clsid) ||
        category.present || category.complete) return false;
    if (!ApplyCategoryPage(category, S_OK, 1, &clsid, clsid) ||
        !category.present || category.complete) return false;
    if (ApplyCategoryPage(category, E_FAIL, 0, nullptr, clsid) ||
        category.present || category.complete) return false;
    category = {};
    for (unsigned long n = 0; n < kMaxItems; ++n) {
        if (!ApplyCategoryPage(category, S_OK, 1, &thirdParty, clsid)) return false;
    }
    if (category.complete || category.present) return false;
    if (ApplyCategoryPage(category, S_FALSE, 0, nullptr, clsid) ||
        !category.complete || category.present) return false;
    category = {};
    if (ApplyCategoryPage(category, S_OK, 0, nullptr, clsid) ||
        category.complete || category.present) return false;

    // Pure attribute classification covers HRESULT and NULL interface cases.
    if (ClassifyAttribute(S_OK, false, thirdParty).resolved ||
        ClassifyAttribute(E_FAIL, true, thirdParty).resolved ||
        ClassifyAttribute(S_FALSE, true, thirdParty).resolved) return false;
    auto attr = ClassifyAttribute(S_OK, true, thirdParty);
    return attr.resolved && IsEqualGUID(attr.owner, thirdParty);
}

}  // namespace

int wmain(int argc, wchar_t* argv[]) {
    if (argc == 2 && wcscmp(argv[1], L"--selftest") == 0) {
        bool passed = SelfTest();  // Synthetic only; never calls CoInitializeEx.
        std::wcout << (passed ? L"PASS T4-B6 synthetic TSF evidence\n"
                              : L"FAIL T4-B6 synthetic TSF evidence\n");
        return passed ? 0 : 2;
    }

    // A deliberate opt-in switch prevents incidental TSF/COM access by CI.
    // Args: --vm-readonly T2_MANIFEST CLSID ATTR_INPUT ATTR_CONVERTED ATTR_TARGET
    // --selftest-guard exercises the EXACT SAME validation without initializing
    // COM. Only --vm-readonly is allowed to pass the guarded boundary.
    if (argc != 7) return 2;
    const bool live = wcscmp(argv[1], L"--vm-readonly") == 0;
    const bool checkOnly = wcscmp(argv[1], L"--selftest-guard") == 0;
    if (!live && !checkOnly) return 2;
    GUID supplied[4]{};
    if (!ValidateBoundT2Inputs(argv[2], &argv[3], supplied)) return 2;
    if (checkOnly) return 0;  // Test-only: no COM, registry or TSF access.
    const GUID clsid = supplied[0];
    const GUID attrs[3] = {supplied[1], supplied[2], supplied[3]};

    AttributeResult values[3]{};
    CategoryResult categories[5]{};
    // COM may instantiate an installed provider even for a "read" API.
    // Execute ONLY inside the operator-approved, snapshotted Windows VM.
    HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (SUCCEEDED(initialized)) {
        ITfDisplayAttributeMgr* attributes = nullptr;
        if (SUCCEEDED(CoCreateInstance(CLSID_TF_DisplayAttributeMgr, nullptr,
                CLSCTX_INPROC_SERVER, IID_ITfDisplayAttributeMgr,
                reinterpret_cast<void**>(&attributes))) && attributes) {
            for (int i = 0; i < 3; ++i) values[i] = ObserveAttribute(attributes, attrs[i]);
        }
        if (attributes) attributes->Release();

        ITfCategoryMgr* manager = nullptr;
        if (SUCCEEDED(CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER,
                IID_ITfCategoryMgr, reinterpret_cast<void**>(&manager))) && manager) {
            for (int i = 0; i < 5; ++i)
                categories[i] = ObserveCategory(manager, *kCategoryGuids[i], clsid);
        }
        if (manager) manager->Release();
        CoUninitialize();
    }

    PrintEvidence(std::wcout, clsid, attrs, values, categories);
    for (const auto& item : values) if (item.resolved) return 1;
    for (const auto& item : categories) if (item.complete && item.present) return 1;
    // No positive "clear" result. Even 8 negative observations stay inconclusive.
    return 2;
}
