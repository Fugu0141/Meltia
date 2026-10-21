// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// T4-B6: VM-ONLY TSF observer producing T4-B3 evidence. No registration functions.
// GetDisplayAttributeInfo may activate an existing third-party COM provider:
// --vm-readonly is intentionally opt-in and MUST NOT run on CI/host machines.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <msctf.h>
#include <cwchar>
#include <iostream>
#include <sstream>
#include <string>

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
        if (hr == S_FALSE && fetched == 0) {
            result.complete = true;
            break;
        }
        if (hr != S_OK || fetched != 1) break;
        if (IsEqualGUID(item, clsid)) result.present = true;
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
    if (hr == S_OK && info != nullptr) {
        result.resolved = true;
        result.owner = owner;
    }
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
    return b.find(L"\"lookup\":\"resolved\",\"owner_clsid\":\"{00000005-0000-4000-8000-000000000005}\"")
               != std::wstring::npos &&
           b.find(L"\"complete\":true,\"present\":true") != std::wstring::npos &&
           b.find(L"\"complete\":true,\"present\":false") != std::wstring::npos;
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
    // Args: --vm-readonly CLSID ATTR_INPUT ATTR_CONVERTED ATTR_TARGET
    if (argc != 6 || wcscmp(argv[1], L"--vm-readonly") != 0) return 2;
    GUID clsid{}, attrs[3]{};
    if (!ParseGuid(argv[2], clsid) || !ParseGuid(argv[3], attrs[0]) ||
        !ParseGuid(argv[4], attrs[1]) || !ParseGuid(argv[5], attrs[2])) return 2;
    for (int i = 0; i < 3; ++i) {
        if (IsEqualGUID(clsid, attrs[i])) return 2;
        for (int j = i + 1; j < 3; ++j)
            if (IsEqualGUID(attrs[i], attrs[j])) return 2;
    }

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
