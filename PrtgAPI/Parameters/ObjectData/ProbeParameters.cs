﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using PrtgAPI.Request;
using PrtgAPI.Utilities;

namespace PrtgAPI.Parameters
{
    /// <summary>
    /// Represents parameters used to construct a <see cref="PrtgUrl"/> for retrieving <see cref="Probe"/> objects.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class ProbeParameters : TableParameters<Probe>, IShallowCloneable<ProbeParameters>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProbeParameters"/> class.
        /// </summary>
        public ProbeParameters() : base(Content.Probes)
        {
            SearchFilters = DefaultSearchFilter();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProbeParameters"/> class with one or more conditions to filter results by.
        /// </summary>
        /// <param name="filters">A list of conditions to filter results by.</param>
        public ProbeParameters(params SearchFilter[] filters) : this()
        {
            SearchFilters = filters.ToList();
        }

        private List<SearchFilter> DefaultSearchFilter()
        {
            return new List<SearchFilter> {new SearchFilter(Property.ParentId, "0")};
        }

        internal override void SetSearchFilter(List<SearchFilter> value)
        {
            if (value.Any(item => item.Property == Property.ParentId && (item.Operator != FilterOperator.Equals || ValueEquals(item, "0", (a, b) => a != b))))
                throw new InvalidOperationException("Cannot filter for probes based on a ParentId other than 0.");

            if (!value.Any(item => item.Property == Property.ParentId && item.Operator == FilterOperator.Equals && ValueEquals(item, "0", (a, b) => a == b)))
                value = value.Union(DefaultSearchFilter()).ToList();

            base.SetSearchFilter(value);
        }

        private bool ValueEquals(SearchFilter filter, string value, Func<string, string, bool> func)
        {
            if (filter.Value.IsIEnumerable())
                return filter.Value.ToIEnumerable().Any(v => func(v.ToString(), value));

            return func(filter.Value.ToString(), value);
        }

        ProbeParameters IShallowCloneable<ProbeParameters>.ShallowClone()
        {
            var newParameters = new ProbeParameters();

            ShallowClone(newParameters);

            return newParameters;
        }

        object IShallowCloneable.ShallowClone() => ((IShallowCloneable<ProbeParameters>)this).ShallowClone();
    }
}
