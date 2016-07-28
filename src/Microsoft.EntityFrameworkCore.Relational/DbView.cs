// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore
{
    public class DbView<TView>
        : IQueryable<TView>, IAsyncEnumerableAccessor<TView>
    {
        private readonly EntityQueryable<TView> _entityQueryable;

        public DbView([NotNull] DbContext context)
        {
            Check.NotNull(context, nameof(context));

            _entityQueryable = new EntityQueryable<TView>(((IInfrastructure<IAsyncQueryProvider>)context).Instance);
        }

        IEnumerator<TView> IEnumerable<TView>.GetEnumerator() => _entityQueryable.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _entityQueryable.GetEnumerator();

        IAsyncEnumerable<TView> IAsyncEnumerableAccessor<TView>.AsyncEnumerable => _entityQueryable;

        Type IQueryable.ElementType => _entityQueryable.ElementType;

        Expression IQueryable.Expression => _entityQueryable.Expression;

        IQueryProvider IQueryable.Provider => _entityQueryable.Provider;
    }
}
