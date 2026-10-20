// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// T3d: invoke the ACTUAL staged TextService::Request with real TipServer IPC.
// No COM activation, TSF registration, OS keystroke injection, or shipping binary.
#include "TextService.h"
#include <cstdint>
#include <iostream>
#include <limits>
#include <string>
#include <windows.h>

namespace meltype {
// Test executable substitutes the globals normally supplied by Dll.cpp.
HINSTANCE g_module = nullptr;
LONG g_locks = 0;
void TipLog(const wchar_t*, ...) {}

std::wstring FromUtf8(const std::string& text) {
    if (text.empty() || text.size() > static_cast<size_t>(std::numeric_limits<int>::max())) return {};
    int length = static_cast<int>(text.size());
    int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), length, nullptr, 0);
    if (count <= 0) return {};
    std::wstring result(count, L'\0');
    return MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, text.data(), length, result.data(), count) ? result : L"";
}

std::string ToUtf8(const std::wstring& text) {
    if (text.empty() || text.size() > static_cast<size_t>(std::numeric_limits<int>::max())) return {};
    int length = static_cast<int>(text.size());
    int count = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), length, nullptr, 0, nullptr, nullptr);
    if (count <= 0) return {};
    std::string result(count, '\0');
    return WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), length, result.data(), count, nullptr, nullptr) ? result : "";
}

// Only the staged, test-macro-enabled class grants access. Original source is unchanged.
struct MeltiaRequestProbe {
    static void SetSid(TextService* service, const std::string& sid) { service->sid_ = sid; }
    static bool Send(TextService* service, const std::string& request, JsonValue& reply) {
        return service->Request(request, reply);
    }
    static void Disconnect(TextService* service) { service->pipe_.Close(); }
    static std::uint64_t Generation(TextService* service) {
        return service->pipe_.ConnectionGeneration();
    }
};
} // namespace meltype

namespace {
using meltype::JsonValue;
enum class Expected { Inactive, PendingK, Kana, KanaCommit };

bool Validate(const JsonValue& value, Expected expected) {
    using Type = JsonValue::Type;
    if (value.type != Type::Object) return false;
    const auto& version = value[L"version"];
    const auto& active = value[L"active"];
    const auto& consumed = value[L"consumed"];
    const auto& commits = value[L"commits"];
    const auto& view = value[L"view"];
    if (version.type != Type::Number || version.number != 1 ||
        active.type != Type::Bool || consumed.type != Type::Bool ||
        commits.type != Type::Array ||
        (view.type != Type::Null && view.type != Type::Object)) return false;

    if (expected == Expected::Inactive)
        return !active.boolean && !consumed.boolean && commits.array.empty() && view.type == Type::Null;
    if (!active.boolean || !consumed.boolean) return false;
    if (expected == Expected::KanaCommit) {
        if (view.type != Type::Null || commits.array.size() != 1) return false;
        const auto& edit = commits.array.front();
        if (edit.type != Type::Object) return false;
        const auto& deleted = edit[L"deleteBefore"];
        const auto& text = edit[L"text"];
        return deleted.type == Type::Number && deleted.number == 0 &&
               text.type == Type::String && text.string == L"か";
    }
    if (view.type != Type::Object || !commits.array.empty()) return false;
    const auto& text = view[L"text"];
    if (text.type != Type::String) return false;
    return expected == Expected::Kana ? text.string == L"か" : !text.string.empty();
}

bool Send(meltype::TextService* service, const char* name, const char* json,
          Expected expected, bool retry = false) {
    // Only the first key may retry while the test server finishes binding.
    const int attempts = retry ? 20 : 1;
    for (int attempt = 0; attempt < attempts; ++attempt) {
        JsonValue reply;
        if (meltype::MeltiaRequestProbe::Send(service, json, reply)) {
            if (Validate(reply, expected)) return true;
            std::cerr << "Unexpected typed response at " << name << std::endl;
            return false;
        }
        if (attempt + 1 < attempts) Sleep(500);
    }
    std::cerr << "Native TextService::Request failed at " << name << std::endl;
    return false;
}
} // namespace

int main() {
    // The constructor does not activate any COM/TSF objects. This test invokes
    // only Request, not ActivateEx, OnKeyDown, Apply or actual OS text editing.
    meltype::TextService* service = new meltype::TextService();
    meltype::MeltiaRequestProbe::SetSid(service, "t3d");
    constexpr auto keyK = R"({"op":"key","sid":"t3d","vk":75,"ch":107,"composing":false})";
    constexpr auto keyA = R"({"op":"key","sid":"t3d","vk":65,"ch":97,"composing":true})";
    constexpr auto stale = R"({"op":"key","sid":"t3d","vk":65,"ch":97,"composing":true})";
    constexpr auto commit = R"({"op":"commit","sid":"t3d"})";
    constexpr auto close = R"({"op":"close","sid":"t3d"})";

    // First-ever key must perform implicit versioned hello BEFORE the key.
    bool ok = Send(service, "first key", keyK, Expected::PendingK, true) &&
              Send(service, "second key", keyA, Expected::Kana) &&
              Send(service, "commit", commit, Expected::KanaCommit) &&
              Send(service, "close", close, Expected::Inactive);
    auto before = meltype::MeltiaRequestProbe::Generation(service);
    if (ok && before == 0) ok = false;
    if (ok) {
        // Close the same PipeClient inside the actual TextService, not a new object.
        meltype::MeltiaRequestProbe::Disconnect(service);
        // A stale composition MUST NOT create a session after automatic re-hello.
        ok = Send(service, "stale composition", stale, Expected::Inactive, true);
        auto after = meltype::MeltiaRequestProbe::Generation(service);
        if (after <= before) {
            std::cerr << "TextService did not advance the pipe connection generation" << std::endl;
            ok = false;
        }
        if (ok) {
            ok = Send(service, "new key", keyK, Expected::PendingK) &&
                 Send(service, "second close", close, Expected::Inactive);
        }
        if (ok) std::cout << "PASS T3d: real TextService::Request; automatic hello; "
                          << "typed kana/commit; rejected stale composition; same-instance epoch "
                          << before << " -> " << after << std::endl;
    }
    meltype::MeltiaRequestProbe::Disconnect(service);
    service->Release();
    if (meltype::g_locks != 0) return 9;
    return ok ? 0 : 1;
}
