// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Xunit;
// ReSharper disable InconsistentNaming

namespace Microsoft.EntityFrameworkCore.Query
{
    public abstract class OwnedQueryTestBase
    {
        [Fact]
        public virtual void Query_for_base_type_loads_all_owned_navs()
        {
            using (var context = CreateContext())
            {
                var people = context.Set<OwnedPerson>().ToList();
                
                Assert.Equal(4, people.Count);
                Assert.True(people.All(p => p.PersonAddress != null));
                Assert.True(people.OfType<Branch>().All(b => b.BranchAddress != null));
                Assert.True(people.OfType<LeafA>().All(a => a.LeafAAddress != null));
                Assert.True(people.OfType<LeafB>().All(b => b.LeafBAddress != null));
            }
        }

        protected abstract DbContext CreateContext();
    }
}
