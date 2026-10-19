// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Portable connection-epoch gate. Not an installer or a TSF activation API.
#pragma once
#include <cstdint>

namespace meltype {
class MeltiaReconnectPolicy final {
public:
    bool NeedsHello(std::uint64_t epoch) const {
        return epoch == 0 || epoch != acceptedEpoch_;
    }
    void AcceptHello(std::uint64_t epoch) {
        if (epoch != 0) acceptedEpoch_ = epoch;
    }
    void Reset() { acceptedEpoch_ = 0; }
private:
    std::uint64_t acceptedEpoch_ = 0;
};
}  // namespace meltype
