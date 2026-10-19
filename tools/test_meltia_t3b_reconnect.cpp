// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Test only: no Win32, pipe, TSF, hook, COM or registration operations.
#include "MeltiaReconnectPolicy.h"
#include <cassert>
#include <cstdint>

int main() {
    using meltype::MeltiaReconnectPolicy;
    MeltiaReconnectPolicy policy;
    assert(policy.NeedsHello(0));
    assert(policy.NeedsHello(1));  // First key/commit must follow a hello.
    policy.AcceptHello(1);
    assert(!policy.NeedsHello(1)); // Subsequent key/commit can use same pipe.
    assert(policy.NeedsHello(2));  // Reconnect: old hello never authorizes new pipe.
    assert(policy.NeedsHello(3));  // Another reconnect without hello remains closed.
    policy.AcceptHello(3);
    assert(!policy.NeedsHello(3));
    policy.Reset();               // Failure revokes the current authorization.
    assert(policy.NeedsHello(3));
    policy.AcceptHello(0);        // A missing/invalid connection cannot be approved.
    assert(policy.NeedsHello(3));
    MeltiaReconnectPolicy second;
    assert(second.NeedsHello(3)); // Separate connections have no shared state.
    return 0;
}
