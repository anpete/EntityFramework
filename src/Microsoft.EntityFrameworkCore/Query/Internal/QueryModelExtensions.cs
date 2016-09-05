// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public static class QueryModelExtensions
    {
        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public static int CountQuerySourceReferences(
            [NotNull] this QueryModel queryModel, IQuerySource querySource)
        {
            var referenceCount = 0;

            Func<Expression, Expression> groupReferenceFinder = null;

            groupReferenceFinder
                = e =>
                    {
                        var qsre = e as QuerySourceReferenceExpression;

                        if (qsre?.ReferencedQuerySource == querySource)
                        {
                            referenceCount++;
                        }

                        (e as SubQueryExpression)?.QueryModel
                            .TransformExpressions(groupReferenceFinder);

                        return e;
                    };

            queryModel.TransformExpressions(groupReferenceFinder);

            return referenceCount;
        }
    }
}
