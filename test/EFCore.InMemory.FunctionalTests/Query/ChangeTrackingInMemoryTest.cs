// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace Microsoft.EntityFrameworkCore.Query
{
    public class ChangeTrackingInMemoryTest : ChangeTrackingTestBase<NorthwindQueryInMemoryFixture<NoopModelCustomizer>>
    {
        public ChangeTrackingInMemoryTest(NorthwindQueryInMemoryFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            TestLoggerFactory.TestOutputHelper = testOutputHelper;
        }

        public override void AsTracking_switches_tracking_on_when_off_in_options()
        {
            base.AsTracking_switches_tracking_on_when_off_in_options();
        }
    }
}
