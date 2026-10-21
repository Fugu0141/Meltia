// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// T4-B1: read-only enumeration of TSF profiles visible to this Windows user.
// No registration, activation, registry writes, process launch, or installer.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <msctf.h>
#include <cwchar>
#include <iostream>

struct Counts {
    unsigned long profiles = 0;
    unsigned long clsid = 0;
    unsigned long profile = 0;
};

static void Observe(const TF_INPUTPROCESSORPROFILE& item,
                    const GUID& expectedClsid, const GUID& expectedProfile,
                    Counts& counts) {
    if (item.dwProfileType != TF_PROFILETYPE_INPUTPROCESSOR) return;
    ++counts.profiles;
    if (IsEqualGUID(item.clsid, expectedClsid)) ++counts.clsid;
    if (IsEqualGUID(item.guidProfile, expectedProfile)) ++counts.profile;
}

static void PrintResult(const wchar_t* status, bool complete,
                        const Counts& counts, const wchar_t* clsid,
                        const wchar_t* profile) {
    // Only caller-supplied GUIDs and aggregate counts; no foreign profile names.
    std::wcout << L"{\"phase\":\"T4-B1\",\"status\":\"" << status
               << L"\",\"complete\":" << (complete ? L"true" : L"false")
               << L",\"coverage\":\"current-user-visible-tsf-profiles\""
               << L",\"expected_clsid\":\"" << clsid
               << L"\",\"expected_profile_guid\":\"" << profile
               << L"\",\"profile_count\":" << counts.profiles
               << L",\"clsid_matches\":" << counts.clsid
               << L",\"profile_guid_matches\":" << counts.profile
               << L",\"registry_writes\":false,\"registration_authorized\":false}\n";
}

static bool SelfTest() {
    GUID ownClsid{}, ownProfile{}, otherClsid{}, otherProfile{};
    // Synthetic values. Never use a Windows profile/registry in this test.
    if (FAILED(CLSIDFromString(L"{00000001-0000-4000-8000-000000000001}", &ownClsid)) ||
        FAILED(CLSIDFromString(L"{00000002-0000-4000-8000-000000000002}", &ownProfile)) ||
        FAILED(CLSIDFromString(L"{00000003-0000-4000-8000-000000000003}", &otherClsid)) ||
        FAILED(CLSIDFromString(L"{00000004-0000-4000-8000-000000000004}", &otherProfile))) {
        return false;
    }
    TF_INPUTPROCESSORPROFILE entry{};
    entry.dwProfileType = TF_PROFILETYPE_KEYBOARDLAYOUT;
    entry.clsid = ownClsid; entry.guidProfile = ownProfile;
    Counts counts;
    Observe(entry, ownClsid, ownProfile, counts);
    if (counts.profiles != 0) return false;
    entry.dwProfileType = TF_PROFILETYPE_INPUTPROCESSOR;
    entry.clsid = otherClsid; entry.guidProfile = otherProfile;
    Observe(entry, ownClsid, ownProfile, counts);
    if (counts.profiles != 1 || counts.clsid != 0 || counts.profile != 0) return false;
    entry.clsid = ownClsid;
    Observe(entry, ownClsid, ownProfile, counts);
    entry.clsid = otherClsid; entry.guidProfile = ownProfile;
    Observe(entry, ownClsid, ownProfile, counts);
    return counts.profiles == 3 && counts.clsid == 1 && counts.profile == 1;
}

int wmain(int argc, wchar_t* argv[]) {
    if (argc == 2 && wcscmp(argv[1], L"--selftest") == 0) {
        const bool ok = SelfTest();
        std::wcout << (ok ? L"PASS T4-B1 synthetic profiles\n"
                          : L"FAIL T4-B1 synthetic profiles\n");
        return ok ? 0 : 2;
    }
    Counts counts;
    GUID expectedClsid{}, expectedProfile{};
    if (argc != 3 || FAILED(CLSIDFromString(argc > 1 ? argv[1] : L"", &expectedClsid)) ||
        FAILED(CLSIDFromString(argc > 2 ? argv[2] : L"", &expectedProfile)) ||
        IsEqualGUID(expectedClsid, expectedProfile)) {
        PrintResult(L"inconclusive", false, counts, L"", L"");
        return 2;
    }
    wchar_t clsid[40]{}, profile[40]{};
    if (!StringFromGUID2(expectedClsid, clsid, 40) ||
        !StringFromGUID2(expectedProfile, profile, 40)) {
        PrintResult(L"inconclusive", false, counts, L"", L"");
        return 2;
    }
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) {
        PrintResult(L"inconclusive", false, counts, clsid, profile);
        return 2;
    }
    ITfInputProcessorProfileMgr* manager = nullptr;
    IEnumTfInputProcessorProfiles* enumerator = nullptr;
    bool complete = false;
    hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr,
                          CLSCTX_INPROC_SERVER, IID_ITfInputProcessorProfileMgr,
                          reinterpret_cast<void**>(&manager));
    if (SUCCEEDED(hr) && manager) {
        // LANGID 0 asks TSF to enumerate every language, not only Japanese.
        hr = manager->EnumProfiles(0, &enumerator);
        if (SUCCEEDED(hr) && enumerator) {
            constexpr unsigned long kMaxProfiles = 100000;
            for (unsigned long i = 0; i < kMaxProfiles; ++i) {
                TF_INPUTPROCESSORPROFILE item{};
                ULONG fetched = 0;
                hr = enumerator->Next(1, &item, &fetched);
                if (hr == S_FALSE && fetched == 0) {
                    complete = true;
                    break;
                }
                if (hr != S_OK || fetched != 1) break;
                Observe(item, expectedClsid, expectedProfile, counts);
            }
        }
    }
    if (enumerator) enumerator->Release();
    if (manager) manager->Release();
    CoUninitialize();
    const bool occupied = counts.clsid != 0 || counts.profile != 0;
    PrintResult(!complete ? L"inconclusive" : occupied ? L"occupied" : L"enumerated-clear",
                complete, counts, clsid, profile);
    return !complete ? 2 : occupied ? 1 : 0;
}
