// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Microsoft.EntityFrameworkCore.Specification.Tests;
using Xunit.Abstractions;
// ReSharper disable InconsistentNaming

namespace Microsoft.EntityFrameworkCore.InMemory.FunctionalTests
{
    public class ComplexNavigationsQueryInMemoryTest : ComplexNavigationsQueryTestBase<InMemoryTestStore, ComplexNavigationsQueryInMemoryFixture>
    {
        public ComplexNavigationsQueryInMemoryTest(ComplexNavigationsQueryInMemoryFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            TestLoggerFactory.TestOutputHelper = testOutputHelper;
        }

        public override void Multiple_optional_navigation_with_Include()
        {
            base.Multiple_optional_navigation_with_Include();

//            using (var context = CreateContext())
//            {
//                var q =
//                    from l1_OneToOne_Optional_FK_OneToOne_Optional_PK_OneToMany_Optional in context.LevelThree
//                    join _l1_OneToOne_Optional_FK_OneToOne_Optional_PK in
//                        (from l1 in context.LevelOne
//                         join l1_OneToOne_Optional_FK in context.LevelTwo
//                         on (int?)EF.Property<int>(l1, "Id") equals EF.Property<int?>(l1_OneToOne_Optional_FK, "Level1_Optional_Id")
//                         into l1_OneToOne_Optional_FK_group
//                         from l1_OneToOne_Optional_FK in
//                         (from l1_OneToOne_Optional_FK_groupItem in l1_OneToOne_Optional_FK_group
//                          
//                          select l1_OneToOne_Optional_FK_groupItem).DefaultIfEmpty()
//                         //where l1_OneToOne_Optional_FK != null
//                         select new CompositeKey( new object[] { EF.Property<int>(l1_OneToOne_Optional_FK, "Id") })
//                         )
//                        .Distinct()
//                    on EF.Property<int?>(l1_OneToOne_Optional_FK_OneToOne_Optional_PK_OneToMany_Optional, "OneToMany_Optional_InverseId")
//                    equals (int?)_l1_OneToOne_Optional_FK_OneToOne_Optional_PK.GetValue(0)
//                    orderby _l1_OneToOne_Optional_FK_OneToOne_Optional_PK.GetValue(0)
//                    select l1_OneToOne_Optional_FK_OneToOne_Optional_PK_OneToMany_Optional;
//
//                q.ToList();
//            }
        }
    }
}
