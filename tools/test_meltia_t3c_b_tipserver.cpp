// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Real native transport against the REAL TipServer.Serve/Handle in a test-only process.
// Validate actual parsed v1 JSON values rather than matching JSON substrings.
#include "Pipe.h"
#include "Json.h"
#include <cstdint>
#include <iostream>
#include <limits>
#include <string>
#include <windows.h>

namespace meltype {
// Only the test binary supplies the symbols normally provided by the TIP DLL.
// No original Globals.cpp/Dll.cpp, TSF registration, logging or user-data writes.
void TipLog(const wchar_t*, ...) {}

std::wstring FromUtf8(const std::string& text) {
    if (text.empty()) return {};
    if (text.size() > static_cast<size_t>(std::numeric_limits<int>::max())) return {};
    auto length = static_cast<int>(text.size());
    int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), length, nullptr, 0);
    if (size == 0) return {};
    std::wstring result(size, L'\0');
    if (!MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), length, result.data(), size))
        return {};
    return result;
}

std::string ToUtf8(const std::wstring& text) {
    if (text.empty()) return {};
    if (text.size() > static_cast<size_t>(std::numeric_limits<int>::max())) return {};
    auto length = static_cast<int>(text.size());
    int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), length, nullptr, 0, nullptr, nullptr);
    if (size == 0) return {};
    std::string result(size, '\0');
    if (!WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), length, result.data(), size, nullptr, nullptr))
        return {};
    return result;
}
}

namespace {
constexpr const char* kHello = R"({"op":"hello","version":1,"process":"notepad"})";
constexpr const char* kKeyBeforeHello = R"({"op":"key","version":1,"sid":"t3cb","vk":75,"ch":107})";
constexpr const char* kKeyK = R"({"op":"key","version":1,"sid":"t3cb","vk":75,"ch":107,"composing":false})";
constexpr const char* kKeyA = R"({"op":"key","version":1,"sid":"t3cb","vk":65,"ch":97,"composing":true})";
constexpr const char* kCommit = R"({"op":"commit","version":1,"sid":"t3cb"})";
constexpr const char* kClose = R"({"op":"close","version":1,"sid":"t3cb"})";
constexpr const char* kStaleComposing = R"({"op":"key","version":1,"sid":"t3cb","vk":75,"ch":107,"composing":true})";

// Structured expectations for each REAL dispatcher operation.
// A separate "hello" is mandatory for each transport connection.
enum class Expected { Inactive, Hello, PendingK, Kana, KanaCommit };

bool Validate(const std::string& wireResponse, Expected expected) {
    meltype::JsonValue response;
    using Type = meltype::JsonValue::Type;
    if (!meltype::ParseJson(wireResponse, response) || response.type != Type::Object)
        return false;

    const auto& version = response[L"version"];
    const auto& active = response[L"active"];
    const auto& consumed = response[L"consumed"];
    const auto& commits = response[L"commits"];
    const auto& view = response[L"view"];
    if (version.type != Type::Number || version.number != 1 ||
        active.type != Type::Bool || consumed.type != Type::Bool ||
        commits.type != Type::Array ||
        (view.type != Type::Object && view.type != Type::Null))
        return false;

    if (expected == Expected::Inactive || expected == Expected::Hello) {
        return active.boolean == (expected == Expected::Hello) &&
               !consumed.boolean && commits.array.empty() &&
               view.type == Type::Null;
    }

    // Both key events and commit must be consumed; otherwise a real TSF client
    // would also forward the physical key and potentially insert it twice.
    if (!active.boolean || !consumed.boolean) return false;

    if (expected == Expected::KanaCommit) {
        if (view.type != Type::Null || commits.array.size() != 1) return false;
        const auto& edit = commits.array.front();
        const auto& deleted = edit[L"deleteBefore"];
        const auto& text = edit[L"text"];
        // No prior committed characters were supplied by this test scenario.
        return edit.type == Type::Object && deleted.type == Type::Number &&
               deleted.number == 0 && text.type == Type::String &&
               text.string == L"か";
    }

    if (!commits.array.empty() || view.type != Type::Object)
        return false;
    const auto& displayedText = view[L"text"];
    if (displayedText.type != Type::String) return false;
    if (expected == Expected::PendingK) {
        // The pending consonant must remain visible before the next letter.
        return !displayedText.string.empty();
    }
    // The actual MeltypeSession must convert the two physical keys k,a into
    // the hiragana か. Merely returning a view object cannot pass this test.
    return expected == Expected::Kana && displayedText.string == L"か";
}

bool ValidationRegression() {
    // Negative controls guard against weakening the assertions in future edits.
    return !Validate(R"({"version":1,"active":true,"consumed":false,"commits":[],"view":{"text":"か"}})",
                     Expected::Kana) &&
           !Validate(R"({"version":1,"active":true,"consumed":true,"commits":[{"deleteBefore":0,"text":""}],"view":null})",
                     Expected::KanaCommit) &&
           !Validate(R"({"version":1,"active":true,"consumed":true,"commits":[{"deleteBefore":2,"text":"か"}],"view":null})",
                     Expected::KanaCommit) &&
           !Validate(R"({"version":1,"active":true,"consumed":true,"commits":[],"view":{"text":"ka"}})",
                     Expected::Kana) &&
           !Validate(R"({"version":"1","active":true,"consumed":true,"commits":[],"view":{"text":"か"}})",
                     Expected::Kana);
}

bool Exchange(meltype::PipeClient& client, const char* request,
              Expected expected, bool retry = false) {
    std::string response;
    const int attempts = retry ? 20 : 1;
    for (int i = 0; i < attempts; ++i) {
        if (client.Transact(request, response, 1200)) {
            if (Validate(response, expected)) return true;
            std::cerr << "Unexpected parsed v1 response for " << request << ": " << response << std::endl;
            return false;
        }
        if (i + 1 < attempts) Sleep(500);
    }
    std::cerr << "Timed out exchanging v1 request" << std::endl;
    return false;
}
}

int main() {
    if (!ValidationRegression()) {
        std::cerr << "JSON response validator negative controls failed" << std::endl;
        return 10;
    }
    meltype::PipeClient client;

    // The same real Win32 client instance must perform BOTH connections.
    if (!Exchange(client, kKeyBeforeHello, Expected::Inactive, true) ||
        !Exchange(client, kHello, Expected::Hello) ||
        !Exchange(client, kKeyK, Expected::PendingK) ||
        !Exchange(client, kKeyA, Expected::Kana) ||
        !Exchange(client, kCommit, Expected::KanaCommit) ||
        !Exchange(client, kClose, Expected::Inactive) ||
        !Exchange(client, kStaleComposing, Expected::Inactive) ||
        !Exchange(client, kKeyK, Expected::PendingK) ||
        !Exchange(client, kClose, Expected::Inactive)) return 1;

    const std::uint64_t firstGeneration = client.ConnectionGeneration();
    if (firstGeneration == 0) return 2;
    client.Close();

    if (!Exchange(client, kKeyBeforeHello, Expected::Inactive, true)) return 3;
    const std::uint64_t secondGeneration = client.ConnectionGeneration();
    if (secondGeneration <= firstGeneration) {
        std::cerr << "Reconnect did not advance PipeClient connection generation" << std::endl;
        return 4;
    }
    if (!Exchange(client, kHello, Expected::Hello) ||
        !Exchange(client, kStaleComposing, Expected::Inactive) ||
        !Exchange(client, kKeyK, Expected::PendingK) ||
        !Exchange(client, kClose, Expected::Inactive)) return 5;
    client.Close();
    std::cout << "PASS T3c-B: 14 parsed JSON exchanges including consumed/view=か/commit=か; "
              << "validator negative controls; same-client reconnect epoch " <<
        firstGeneration << " -> " << secondGeneration << std::endl;
    return 0;
}
