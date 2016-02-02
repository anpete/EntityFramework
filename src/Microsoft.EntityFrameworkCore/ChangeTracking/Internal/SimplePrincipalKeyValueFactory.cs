// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microsoft.EntityFrameworkCore.ChangeTracking.Internal
{
    public class SimplePrincipalKeyValueFactory<TKey> : IPrincipalKeyValueFactory<TKey>
    {
        private readonly PropertyAccessors _propertyAccessors;

        public SimplePrincipalKeyValueFactory([NotNull] PropertyAccessors propertyAccessors)
        {
            _propertyAccessors = propertyAccessors;

            if (typeof(IStructuralEquatable).GetTypeInfo().IsAssignableFrom(typeof(TKey).GetTypeInfo()))
            {
                EqualityComparer = new NoNullsStructuralEqualityComparer();
                Comparer = new NoNullsStructuralComparer();
            }
            else
            {
                EqualityComparer = new NoNullsEqualityComparer();
                Comparer = new NoNullsComparer();
            }
        }

        public virtual object CreateFromBuffer(ValueBuffer valueBuffer)
            => _propertyAccessors.ValueBufferGetter(valueBuffer);

        public virtual TKey CreateFromCurrentValues(InternalEntityEntry entry)
            => ((Func<InternalEntityEntry, TKey>)_propertyAccessors.CurrentValueGetter)(entry);

        public virtual TKey CreateFromOriginalValues(InternalEntityEntry entry)
            => ((Func<InternalEntityEntry, TKey>)_propertyAccessors.OriginalValueGetter)(entry);

        public virtual TKey CreateFromRelationshipSnapshot(InternalEntityEntry entry)
            => ((Func<InternalEntityEntry, TKey>)_propertyAccessors.RelationshipSnapshotGetter)(entry);

        public virtual IEqualityComparer<TKey> EqualityComparer { get; }

        public virtual IComparer<TKey> Comparer { get; }

        private sealed class NoNullsEqualityComparer : IEqualityComparer<TKey>
        {
            public bool Equals(TKey x, TKey y) => x.Equals(y);
            public int GetHashCode(TKey obj) => obj.GetHashCode();
        }

        private sealed class NoNullsComparer : IComparer<TKey>
        {
            private readonly Comparer<TKey> _comparer = Comparer<TKey>.Default;

            public int Compare(TKey x, TKey y) => _comparer.Compare(x, y);
        }

        private sealed class NoNullsStructuralEqualityComparer : IEqualityComparer<TKey>
        {
            private readonly IEqualityComparer _structuralEqualityComparer
                = StructuralComparisons.StructuralEqualityComparer;

            public bool Equals(TKey x, TKey y) => _structuralEqualityComparer.Equals(x, y);

            public int GetHashCode(TKey obj) => _structuralEqualityComparer.GetHashCode(obj);
        }

        private sealed class NoNullsStructuralComparer : IComparer<TKey>
        {
            private readonly IComparer _structuralComparer = StructuralComparisons.StructuralComparer;

            public int Compare(TKey x, TKey y) => _structuralComparer.Compare(x, y);
        }
    }
}
