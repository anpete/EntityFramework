// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations.Schema;

namespace Microsoft.EntityFrameworkCore.TestModels.Northwind
{
    public class CustomerView
    {
        public string CustomerID { get; set; }
        public string CompanyName { get; set; }
        public string ContactName { get; set; }
        public string ContactTitle { get; set; }
        public string Address { get; set; }
        public string City { get; set; }

        //public virtual ICollection<Order> Orders { get; set; }

        [NotMapped]
        public bool IsLondon => City == "London";

        protected bool Equals(CustomerView other) => string.Equals(CustomerID, other.CustomerID);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return obj.GetType() == GetType()
                   && Equals((CustomerView)obj);
        }

        public static bool operator ==(CustomerView left, CustomerView right) => Equals(left, right);

        public static bool operator !=(CustomerView left, CustomerView right) => !Equals(left, right);

        public override int GetHashCode() => CustomerID.GetHashCode();

        public override string ToString() => "CustomerView " + CustomerID;
    }
}
