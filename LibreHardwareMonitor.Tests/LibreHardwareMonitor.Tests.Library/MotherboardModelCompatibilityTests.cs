// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Hardware.Motherboard;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class MotherboardModelCompatibilityTests
{
    [Fact]
    public void NewModel_DoesNotRenumberExistingPublicEnumValues()
    {
        Assert.Equal(54, (int)Model.ROG_STRIX_Z390_E_GAMING);
        Assert.Equal(335, (int)Model.Unknown);
        Assert.Equal(336, (int)Model.ROG_STRIX_Z370_G_GAMING);
    }
}
