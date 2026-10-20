// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Real native transport against the REAL TipServer.Serve/Handle in a test-only process.
#include "Pipe.h"
#include <cstdint>
#include <iostream>
#include <string>
#include <windows.h>

namespace meltype {
void TipLog(const wchar_t*, ...) {} // No external log or Meltype user-data writes.
}

namespace {
constexpr const char* kHello = R"({"op":"hello","version":1,"process":"notepad"})";
constexpr const char* kKeyBeforeHello = R"({"op":"key","version":1,"sid":"t3cb","vk":75,"ch":107})";
constexpr const char* kKeyK = R"({"op":"key","version":1,"sid":"t3cb","vk":75,"ch":107,"composing":false})";
constexpr const char* kKeyA = R"({"op":"key","version":1,"sid":"t3cb","vk":65,"ch":97,"composing":true})";
constexpr const char* kCommit = R"({"op":"commit","version":1,"sid":"t3cb"})";
constexpr const char* kClose = R"({"op":"close","version":1,"sid":"t3cb"})";
constexpr const char* kStaleComposing = R"({"op":"key","version":1,"sid":"t3cb","vk":75,"ch":107,"composing":true})";

bool Exchange(meltype::PipeClient& client, const char* request, bool expectedActive,
              bool expectComposition = false, bool retry = false, bool expectCommit = false) {
    std::string response;
    const int attempts = retry ? 20 : 1;
    for (int i = 0; i < attempts; ++i) {
        if (client.Transact(request, response, 1200)) {
            bool active = response.find(R"("active":true)") != std::string::npos;
            bool inactive = response.find(R"("active":false)") != std::string::npos;
            bool versioned = response.find(R"("version":1)") != std::string::npos;
            bool schema = response.find(R"("consumed":)") != std::string::npos &&
                          response.find(R"("commits":)") != std::string::npos &&
                          response.find(R"("view":)") != std::string::npos;
            bool hasView = response.find(R"("view":{)") != std::string::npos;
            bool hasCommittedText = response.find(R"("commits":[{)") != std::string::npos;
            if (versioned && schema && active == expectedActive && inactive != expectedActive &&
                (!expectComposition || hasView) && (!expectCommit || hasCommittedText)) return true;
            std::cerr << "Unexpected response: " << response << std::endl;
            return false;
        }
        if (i + 1 < attempts) Sleep(500);
    }
    std::cerr << "Timed out exchanging v1 request" << std::endl;
    return false;
}
}

int main() {
    meltype::PipeClient client;

    // The same real Win32 client instance must perform BOTH connections.
    if (!Exchange(client, kKeyBeforeHello, false, false, true) ||
        !Exchange(client, kHello, true) ||
        !Exchange(client, kKeyK, true, true) ||
        !Exchange(client, kKeyA, true, true) ||
        !Exchange(client, kCommit, true, false, false, true) ||
        !Exchange(client, kClose, false) ||
        !Exchange(client, kStaleComposing, false) ||
        !Exchange(client, kKeyK, true, true) ||
        !Exchange(client, kClose, false)) return 1;

    const std::uint64_t firstGeneration = client.ConnectionGeneration();
    if (firstGeneration == 0) return 2;
    client.Close();

    if (!Exchange(client, kKeyBeforeHello, false, false, true)) return 3;
    const std::uint64_t secondGeneration = client.ConnectionGeneration();
    if (secondGeneration <= firstGeneration) {
        std::cerr << "Reconnect did not advance PipeClient connection generation" << std::endl;
        return 4;
    }
    if (!Exchange(client, kHello, true) ||
        !Exchange(client, kStaleComposing, false) ||
        !Exchange(client, kKeyK, true, true) ||
        !Exchange(client, kClose, false)) return 5;
    client.Close();
    std::cout << "PASS T3c-B: 14 real exchanges; same-client reconnect epoch " <<
        firstGeneration << " -> " << secondGeneration << std::endl;
    return 0;
}
