// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.EntityFrameworkCore.TestModels.Northwind
{
    public class OrderView
    {
        public string CustomerID { get; set; }

        public Customer Customer { get; set; }

        protected bool Equals(OrderView other)
        {
            return string.Equals(CustomerID, other.CustomerID);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;

            return obj.GetType() == GetType()
                   && Equals((OrderView)obj);
        }

        public static bool operator ==(OrderView left, OrderView right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(OrderView left, OrderView right)
        {
            return !Equals(left, right);
        }

        public override int GetHashCode()
        {
            return CustomerID.GetHashCode();
        }

        public override string ToString()
        {
            return "OrderView " + CustomerID;
        }
    }
}