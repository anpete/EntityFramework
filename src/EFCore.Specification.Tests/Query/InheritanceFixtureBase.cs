// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.TestModels.Inheritance;

namespace Microsoft.EntityFrameworkCore.Query
{
    public abstract class InheritanceFixtureBase : SharedStoreFixtureBase<InheritanceContext>
    {
        protected override string StoreName { get; } = "InheritanceTest";
        protected virtual bool EnableFilters => false;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            modelBuilder.Entity<Kiwi>();
            modelBuilder.Entity<Eagle>();
            modelBuilder.Entity<Bird>();
            modelBuilder.Entity<Animal>().HasKey(e => e.Species);
            modelBuilder.Entity<Rose>();
            modelBuilder.Entity<Daisy>();
            modelBuilder.Entity<Flower>();
            modelBuilder.Entity<Plant>().HasKey(e => e.Species);
            modelBuilder.Entity<Country>();
            modelBuilder.Entity<Drink>();
            modelBuilder.Entity<Tea>();
            modelBuilder.Entity<Lilt>();
            modelBuilder.Entity<Coke>();

            if (EnableFilters)
            {
                modelBuilder.Entity<Animal>().HasQueryFilter(a => a.CountryId == 1);
            }

            modelBuilder.View<AnimalView>()
                .ToQuery(c => c.Set<Bird>().Include(navigationPropertyPath: "Prey").Select(b => MaterializeView(b)));

            modelBuilder.View<BirdView>();
            modelBuilder.View<KiwiView>();
        }

        private static AnimalView MaterializeView(Bird bird)
        {
            switch (bird)
            {
                case Kiwi kiwi:
                    return new KiwiView
                    {
                        Name = kiwi.Name,
                        CountryId = kiwi.CountryId,
                        EagleId = kiwi.EagleId,
                        FoundOn = kiwi.FoundOn,
                        IsFlightless = kiwi.IsFlightless
                    };
                case Eagle eagle:
                    return new EagleView
                    {
                        Name = eagle.Name,
                        CountryId = eagle.CountryId,
                        EagleId = eagle.EagleId,
                        Group = eagle.Group,
                        IsFlightless = eagle.IsFlightless,
                        Prey = eagle.Prey
                    };
            }

            throw new InvalidOperationException();
        }

        protected override void Seed(InheritanceContext context) => InheritanceContext.SeedData(context);
    }
}
