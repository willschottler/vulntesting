// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Analyzer.Utilities;
using Analyzer.Utilities.Extensions;
using Analyzer.Utilities.PooledObjects;

#pragma warning disable CA1067 // Override Object.Equals(object) when implementing IEquatable<T> - CacheBasedEquatable handles equality

namespace Microsoft.CodeAnalysis.FlowAnalysis.DataFlow.GlobalFlowStateAnalysis
{
    internal sealed class GlobalFlowStateAnalysisValueSet : CacheBasedEquatable<GlobalFlowStateAnalysisValueSet>
    {
        public static readonly GlobalFlowStateAnalysisValueSet Unset = new GlobalFlowStateAnalysisValueSet(
            ImmutableHashSet<IAbstractAnalysisValue>.Empty, ImmutableHashSet<GlobalFlowStateAnalysisValueSet>.Empty, 0, GlobalFlowStateAnalysisValueSetKind.Unset);
        public static readonly GlobalFlowStateAnalysisValueSet Empty = new GlobalFlowStateAnalysisValueSet(
            ImmutableHashSet<IAbstractAnalysisValue>.Empty, ImmutableHashSet<GlobalFlowStateAnalysisValueSet>.Empty, 0, GlobalFlowStateAnalysisValueSetKind.Empty);
        public static readonly GlobalFlowStateAnalysisValueSet Unknown = new GlobalFlowStateAnalysisValueSet(
            ImmutableHashSet<IAbstractAnalysisValue>.Empty, ImmutableHashSet<GlobalFlowStateAnalysisValueSet>.Empty, 0, GlobalFlowStateAnalysisValueSetKind.Unknown);

        public GlobalFlowStateAnalysisValueSet(
            ImmutableHashSet<IAbstractAnalysisValue> analysisValues,
            ImmutableHashSet<GlobalFlowStateAnalysisValueSet> parents,
            int height,
            GlobalFlowStateAnalysisValueSetKind kind)
        {
            Debug.Assert((!analysisValues.IsEmpty || !parents.IsEmpty) == (kind == GlobalFlowStateAnalysisValueSetKind.Known));
            Debug.Assert(analysisValues.All(value => value != default));
            Debug.Assert(parents.All(parent => parent != null));
            Debug.Assert(height >= 0);
            Debug.Assert(height == 0 || kind == GlobalFlowStateAnalysisValueSetKind.Known);
            Debug.Assert(height == 0 == parents.IsEmpty);

            AnalysisValues = analysisValues;
            Parents = parents;
            Height = height;
            Kind = kind;
        }

        public GlobalFlowStateAnalysisValueSet(IAbstractAnalysisValue analysisValue)
            : this(ImmutableHashSet.Create(analysisValue), ImmutableHashSet<GlobalFlowStateAnalysisValueSet>.Empty, height: 0, GlobalFlowStateAnalysisValueSetKind.Known)
        {
        }

        public GlobalFlowStateAnalysisValueSet(GlobalFlowStateAnalysisValueSet parent1, GlobalFlowStateAnalysisValueSet parent2)
            : this(ImmutableHashSet<IAbstractAnalysisValue>.Empty,
                   ImmutableHashSet.Create(parent1, parent2),
                   height: Math.Max(parent1.Height, parent2.Height) + 1,
                   GlobalFlowStateAnalysisValueSetKind.Known)
        {
        }

        public ImmutableHashSet<IAbstractAnalysisValue> AnalysisValues { get; }
        public ImmutableHashSet<GlobalFlowStateAnalysisValueSet> Parents { get; }
        public int Height { get; }
        public GlobalFlowStateAnalysisValueSetKind Kind { get; }

        private GlobalFlowStateAnalysisValueSet WithRootParent(GlobalFlowStateAnalysisValueSet newRoot)
        {
            Debug.Assert(Kind == GlobalFlowStateAnalysisValueSetKind.Known);

            var newHeight = Height + newRoot.Height + 1;
            if (Parents.IsEmpty)
            {
                return new GlobalFlowStateAnalysisValueSet(AnalysisValues, ImmutableHashSet.Create(newRoot), newHeight, GlobalFlowStateAnalysisValueSetKind.Known);
            }

            using var parentsBuilder = PooledHashSet<GlobalFlowStateAnalysisValueSet>.GetInstance();
            foreach (var parent in Parents)
            {
                parentsBuilder.Add(parent.WithRootParent(newRoot));
            }

            return new GlobalFlowStateAnalysisValueSet(AnalysisValues, parentsBuilder.ToImmutable(), newHeight, GlobalFlowStateAnalysisValueSetKind.Known);
        }

        internal GlobalFlowStateAnalysisValueSet WithAdditionalAnalysisValues(GlobalFlowStateAnalysisValueSet newAnalysisValuesSet, bool negate)
        {
            return WithAdditionalAnalysisValuesCore(negate ? newAnalysisValuesSet.GetNegatedValue() : newAnalysisValuesSet);
        }

        private GlobalFlowStateAnalysisValueSet WithAdditionalAnalysisValuesCore(GlobalFlowStateAnalysisValueSet newAnalysisValues)
        {
            Debug.Assert(Kind != GlobalFlowStateAnalysisValueSetKind.Unknown);

            if (Kind != GlobalFlowStateAnalysisValueSetKind.Known)
            {
                return newAnalysisValues;
            }

            if (newAnalysisValues.Height == 0)
            {
                return new GlobalFlowStateAnalysisValueSet(
                    AnalysisValues.AddRange(newAnalysisValues.AnalysisValues), Parents, Height, GlobalFlowStateAnalysisValueSetKind.Known);
            }

            return newAnalysisValues.WithRootParent(this);
        }

        internal GlobalFlowStateAnalysisValueSet GetNegatedValue()
        {
            Debug.Assert(Kind == GlobalFlowStateAnalysisValueSetKind.Known);

            if (Height == 0 && AnalysisValues.Count == 1)
            {
                var negatedAnalysisValues = ImmutableHashSet.Create(AnalysisValues.Single().GetNegatedValue());
                return new GlobalFlowStateAnalysisValueSet(negatedAnalysisValues, Parents, Height, Kind);
            }
            else if (Height > 0 && AnalysisValues.Count == 0)
            {
                return GetNegateValueFromParents(Parents);
            }
            else
            {
                var parentsBuilder = ImmutableHashSet.CreateBuilder<GlobalFlowStateAnalysisValueSet>();
                foreach (var analysisValue in AnalysisValues)
                {
                    parentsBuilder.Add(new GlobalFlowStateAnalysisValueSet(analysisValue.GetNegatedValue()));
                }

                int height;
                if (Height > 0)
                {
                    var negatedValueFromParents = GetNegateValueFromParents(Parents);
                    parentsBuilder.Add(negatedValueFromParents);
                    height = negatedValueFromParents.Height + 1;
                }
                else
                {
                    Debug.Assert(AnalysisValues.Count > 1);
                    Debug.Assert(parentsBuilder.Count > 1);
                    height = 1;
                }

                return new GlobalFlowStateAnalysisValueSet(ImmutableHashSet<IAbstractAnalysisValue>.Empty, parentsBuilder.ToImmutable(), height, Kind);
            }

            static GlobalFlowStateAnalysisValueSet GetNegateValueFromParents(ImmutableHashSet<GlobalFlowStateAnalysisValueSet> parents)
            {
                Debug.Assert(parents.Count > 0);
                var analysisValuesBuilder = ImmutableHashSet.CreateBuilder<IAbstractAnalysisValue>();
                var parentsBuilder = ImmutableHashSet.CreateBuilder<GlobalFlowStateAnalysisValueSet>();

                var height = 0;
                foreach (var parent in parents)
                {
                    if (parent.AnalysisValues.Count == 1 && parent.Height == 0)
                    {
                        analysisValuesBuilder.Add(parent.AnalysisValues.Single().GetNegatedValue());
                    }
                    else
                    {
                        var negatedParent = parent.GetNegatedValue();
                        parentsBuilder.Add(negatedParent);
                        height = Math.Max(height, negatedParent.Height + 1);
                    }
                }

                return new GlobalFlowStateAnalysisValueSet(analysisValuesBuilder.ToImmutable(), parentsBuilder.ToImmutable(), height, GlobalFlowStateAnalysisValueSetKind.Known);
            }
        }

        protected override void ComputeHashCodeParts(ref RoslynHashCode hashCode)
        {
            hashCode.Add(HashUtilities.Combine(AnalysisValues));
            hashCode.Add(HashUtilities.Combine(Parents));
            hashCode.Add(Height.GetHashCode());
            hashCode.Add(Kind.GetHashCode());
        }

        protected override bool ComputeEqualsByHashCodeParts(CacheBasedEquatable<GlobalFlowStateAnalysisValueSet> obj)
        {
            var other = (GlobalFlowStateAnalysisValueSet)obj;
            return HashUtilities.Combine(AnalysisValues) == HashUtilities.Combine(other.AnalysisValues)
                && HashUtilities.Combine(Parents) == HashUtilities.Combine(other.Parents)
                && Height.GetHashCode() == other.Height.GetHashCode()
                && Kind.GetHashCode() == other.Kind.GetHashCode();
        }

        public override string ToString()
        {
            return GetParentString() + GetAnalysisValuesString();

            string GetParentString()
            {
                if (Parents.IsEmpty)
                {
                    return string.Empty;
                }

                using var parentsBuilder = ArrayBuilder<string>.GetInstance(Parents.Count);
                foreach (var parent in Parents)
                {
                    parentsBuilder.Add(parent.ToString());
                }

                var result = string.Join(" || ", parentsBuilder.Order());
                if (parentsBuilder.Count > 1)
                {
                    result = $"({result})";
                }

                return result;
            }

            string GetAnalysisValuesString()
            {
                if (AnalysisValues.IsEmpty)
                {
                    return string.Empty;
                }

                var result = string.Join(" && ", AnalysisValues.Select(f => f.ToString()).Order());
                if (!Parents.IsEmpty)
                {
                    result = $" && {result}";
                }

                return result;
            }
        }
    }
}
