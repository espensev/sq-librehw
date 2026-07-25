// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Windows.Forms.Utilities;
using Xunit;

namespace LibreHardwareMonitor.Tests;

public sealed class HttpServerAuthenticationTests
{
    [Fact]
    public void Constructor_PreservesStoredPasswordHashWithoutHashingItAgain()
    {
        const string storedHash = "already-hashed-value";
        var server = new HttpServer(null, null, "127.0.0.1", 0, passwordSHA256: storedHash);

        Assert.Equal(storedHash, server.PasswordSHA256);
    }

    [Fact]
    public void SetPassword_HashesPlainTextExactlyOnce()
    {
        const string expectedSha256 = "5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8";
        var server = new HttpServer(null, null, "127.0.0.1", 0);

        server.SetPassword("password");

        Assert.Equal(expectedSha256, server.PasswordSHA256);
    }
}
