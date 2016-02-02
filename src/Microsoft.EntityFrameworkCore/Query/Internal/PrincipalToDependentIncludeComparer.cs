// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    public class PrincipalToDependentIncludeComparer<TKey> : IIncludeKeyComparer
    {
        private readonly TKey _principalKeyValue;
        private readonly IDependentKeyValueFactory<TKey> _dependentKeyValueFactory;
        private readonly IComparer<TKey> _comparer;

        public PrincipalToDependentIncludeComparer(
            [NotNull] TKey principalKeyValue,
            [NotNull] IDependentKeyValueFactory<TKey> dependentKeyValueFactory,
            [NotNull] IPrincipalKeyValueFactory<TKey> principalKeyValueFactory)
        {
            _principalKeyValue = principalKeyValue;
            _dependentKeyValueFactory = dependentKeyValueFactory;
            _comparer = principalKeyValueFactory.Comparer;
        }

        public virtual bool ShouldInclude(ValueBuffer valueBuffer)
        {
            throw new NotImplementedException();
        }

        public virtual int Compare(ValueBuffer valueBuffer)
        {
            TKey dependentKey;
            if (!_dependentKeyValueFactory.TryCreateFromBuffer(valueBuffer, out dependentKey))
            {
                return 1;
            }

            return _comparer.Compare(dependentKey, _principalKeyValue);
        }
    }
}
