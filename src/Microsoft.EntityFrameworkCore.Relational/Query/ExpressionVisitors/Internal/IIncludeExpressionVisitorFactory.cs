// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq.Expressions;
using JetBrains.Annotations;

namespace Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal
{
    public interface IIncludeExpressionVisitorFactory
    {
        IncludeExpressionVisitor Create(
            [NotNull] IncludeSpecification includeSpecification,
            [NotNull] RelationalQueryCompilationContext relationalQueryCompilationContext,
            [NotNull] IReadOnlyList<int> queryIndexes,
            [NotNull] LambdaExpression accessorLambda,
            bool querySourceRequiresTracking);
    }
}
