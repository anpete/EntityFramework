// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace Microsoft.EntityFrameworkCore.ChangeTracking.Internal
{
    public class CompositePrincipalKeyValueFactory : IPrincipalKeyValueFactory<object[]>
    {
        private readonly IReadOnlyList<IProperty> _properties;

        public CompositePrincipalKeyValueFactory([NotNull] IKey key)
        {
            _properties = key.Properties;

            var structuralTypeInfo = typeof(IStructuralEquatable).GetTypeInfo();

            if (_properties.Any(p => structuralTypeInfo.IsAssignableFrom(p.ClrType.GetTypeInfo())))
            {
                EqualityComparer = new StructuralCompositeEqualityComparer();
                Comparer = new StructuralCompositeComparer();
            }
            else
            {
                EqualityComparer = new CompositeEqualityComparer();
                Comparer = new CompositeComparer();
            }
        }

        public virtual object CreateFromBuffer(ValueBuffer valueBuffer)
        {
            var values = new object[_properties.Count];
            var index = 0;

            foreach (var property in _properties)
            {
                if ((values[index++] = valueBuffer[property.GetIndex()]) == null)
                {
                    return null;
                }
            }

            return values;
        }

        public virtual object[] CreateFromCurrentValues(InternalEntityEntry entry)
            => CreateFromEntry(entry, (e, p) => e.GetCurrentValue(p));

        public virtual object[] CreateFromOriginalValues(InternalEntityEntry entry)
            => CreateFromEntry(entry, (e, p) => e.GetOriginalValue(p));

        public virtual object[] CreateFromRelationshipSnapshot(InternalEntityEntry entry)
            => CreateFromEntry(entry, (e, p) => e.GetRelationshipSnapshotValue(p));

        private object[] CreateFromEntry(
            InternalEntityEntry entry,
            Func<InternalEntityEntry, IProperty, object> getValue)
        {
            var values = new object[_properties.Count];
            var index = 0;

            foreach (var property in _properties)
            {
                values[index++] = getValue(entry, property);
            }

            return values;
        }

        public virtual IEqualityComparer<object[]> EqualityComparer { get; }

        public virtual IComparer<object[]> Comparer { get; }

        private sealed class CompositeEqualityComparer : IEqualityComparer<object[]>
        {
            public bool Equals(object[] x, object[] y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x.Length != y.Length)
                {
                    return false;
                }

                for (var i = 0; i < x.Length; i++)
                {
                    if (!x[i].Equals(y[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(object[] obj)
            {
                var hashCode = 0;

                for (var i = 0; i < obj.Length; i++)
                {
                    hashCode = (hashCode * 397) ^ obj[i].GetHashCode();
                }

                return hashCode;
            }
        }

        private sealed class CompositeComparer : IComparer<object[]>
        {
            private readonly IComparer _comparer = Comparer<object>.Default;

            public int Compare(object[] x, object[] y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x.Length != y.Length)
                {
                    return 1;
                }

                for (var i = 0; i < x.Length; i++)
                {
                    var result = _comparer.Compare(x[i], y[i]);

                    if (result != 0)
                    {
                        return result;
                    }
                }

                return 0;
            }
        }

        private sealed class StructuralCompositeEqualityComparer : IEqualityComparer<object[]>
        {
            private readonly IEqualityComparer _structuralEqualityComparer
                = StructuralComparisons.StructuralEqualityComparer;

            public bool Equals(object[] x, object[] y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x.Length != y.Length)
                {
                    return false;
                }

                for (var i = 0; i < x.Length; i++)
                {
                    if (!_structuralEqualityComparer.Equals(x[i], y[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(object[] obj)
            {
                var hashCode = 0;

                for (var i = 0; i < obj.Length; i++)
                {
                    hashCode = (hashCode * 397) ^ _structuralEqualityComparer.GetHashCode(obj[i]);
                }

                return hashCode;
            }
        }

        private sealed class StructuralCompositeComparer : IComparer<object[]>
        {
            private readonly IComparer _structuralComparer = StructuralComparisons.StructuralComparer;

            public int Compare(object[] x, object[] y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x.Length != y.Length)
                {
                    return 1;
                }

                for (var i = 0; i < x.Length; i++)
                {
                    var result = _structuralComparer.Compare(x[i], y[i]);

                    if (result != 0)
                    {
                        return result;
                    }
                }

                return 0;
            }
        }
    }
}
