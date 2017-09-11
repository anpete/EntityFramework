// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Utilities;

// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore
{
    /// <summary>
    ///     In-memory database specific extension methods for <see cref="ViewTypeBuilder" />.
    /// </summary>
    public static class InMemoryViewTypeBuilderExtensions
    {
        /// <summary>
        ///     Configures the query used to provide data for a in-memory view.
        /// </summary>
        /// <param name="viewTypeBuilder"> The builder for the view type being configured. </param>
        /// <param name="query"> The query representing the in-memory view. </param>
        /// <returns> The same builder instance so that multiple calls can be chained. </returns>
        public static ViewTypeBuilder<TView> ToQuery<TView>(
            [NotNull] this ViewTypeBuilder<TView> viewTypeBuilder,
            [NotNull] Func<DbContext, IQueryable<TView>> query)
            where TView : class
        {
            Check.NotNull(viewTypeBuilder, nameof(viewTypeBuilder));
            Check.NotNull(query, nameof(query));

            viewTypeBuilder.Metadata["query"] = query;

            return viewTypeBuilder;
        }
    }
}