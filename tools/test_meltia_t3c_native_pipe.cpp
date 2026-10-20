// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Real Win32 PipeClient from the T3b isolated stage, no TSF/COM registration.
#include "Pipe.h"
#include <iostream>
#include <string>
#include <windows.h>

namespace meltype {
// Pipe.cpp's logging dependency is deliberately inert in this test executable.
void TipLog(const wchar_t*, ...) {}
}

namespace {
constexpr const char* kActive =
    R"({"version":1,"active":true,"consumed":false,"commits":[],"view":null})";
constexpr const char* kInactive =
    R"({"version":1,"active":false,"consumed":false,"commits":[],"view":null})";
constexpr const char* kHello = R"({"op":"hello","version":1,"process":"notepad"})";
constexpr const char* kKey = u8R"({"op":"key","version":1,"sid":"t3c","before":"日本語"})";
constexpr const char* kWrongVersion = R"({"op":"key","version":2,"sid":"t3c"})";

bool Exchange(meltype::PipeClient& client, const char* request, bool active, bool retry = false) {
    std::string reply;
    const int attempts = retry ? 15 : 1;
    for (int n = 0; n < attempts; ++n) {
        if (client.Transact(request, reply, 800)) {
            if (reply == (active ? kActive : kInactive)) return true;
            std::cerr << "Unexpected response: " << reply << std::endl;
            return false;
        }
        if (n + 1 < attempts) Sleep(500);
    }
    std::cerr << "Timed out waiting for a v1 pipe response" << std::endl;
    return false;
}
}

int main() {
    meltype::PipeClient first;
    if (!Exchange(first, kHello, true) ||
        !Exchange(first, kKey, true) ||
        !Exchange(first, kWrongVersion, false) ||
        !Exchange(first, kKey, false) ||
        !Exchange(first, kHello, true) ||
        !Exchange(first, kKey, true)) return 1;
    first.Close();

    // A different transport connection must start inactive, even if A was active.
    meltype::PipeClient second;
    if (!Exchange(second, kKey, false, true) ||
        !Exchange(second, kHello, true) ||
        !Exchange(second, kKey, true)) return 2;
    second.Close();
    std::cout << "PASS native PipeClient: 9 real request/reply pairs" << std::endl;
    return 0;
}
