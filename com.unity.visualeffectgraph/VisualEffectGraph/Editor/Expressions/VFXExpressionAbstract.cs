using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEditor.VFX
{
    abstract partial class VFXExpression
    {
        [Flags]
        public enum Flags
        {
            None =          0,
            Value =         1 << 0, // Expression is a value, get/set can be called on it
            Foldable =      1 << 1, // Expression is not a constant but can be folded anyway
            Constant =      1 << 2, // Expression is a constant, it can be folded
            InvalidOnGPU =  1 << 3, // Expression can be evaluated on GPU
            InvalidOnCPU =  1 << 4, // Expression can be evaluated on CPU
            PerElement =    1 << 5, // Expression is per element
        }

        public static bool IsFloatValueType(VFXValueType valueType)
        {
            return valueType == VFXValueType.kFloat
                || valueType == VFXValueType.kFloat2
                || valueType == VFXValueType.kFloat3
                || valueType == VFXValueType.kFloat4;
        }

        public static int TypeToSize(VFXValueType type)
        {
            switch (type)
            {
                case VFXValueType.kFloat: return 1;
                case VFXValueType.kFloat2: return 2;
                case VFXValueType.kFloat3: return 3;
                case VFXValueType.kFloat4: return 4;
                case VFXValueType.kInt: return 1;
                case VFXValueType.kUint: return 1;
                case VFXValueType.kTransform: return 16;
            }
            throw new NotImplementedException(type.ToString());
        }

        public static string TypeToCode(VFXValueType type)
        {
            switch (type)
            {
                case VFXValueType.kFloat: return "float";
                case VFXValueType.kFloat2: return "float2";
                case VFXValueType.kFloat3: return "float3";
                case VFXValueType.kFloat4: return "float4";
                case VFXValueType.kInt: return "int";
                case VFXValueType.kUint: return "uint";
                case VFXValueType.kTransform: return "float4x4";
            }
            throw new NotImplementedException(type.ToString());
        }

        public static Type TypeToType(VFXValueType type)
        {
            switch (type)
            {
                case VFXValueType.kFloat: return typeof(float);
                case VFXValueType.kFloat2: return typeof(Vector2);
                case VFXValueType.kFloat3: return typeof(Vector3);
                case VFXValueType.kFloat4: return typeof(Vector4);
                case VFXValueType.kCurve: return typeof(AnimationCurve);
                case VFXValueType.kUint: return typeof(uint);
            }
            throw new NotImplementedException(type.ToString());
        }

        public static bool IsTypeValidOnGPU(VFXValueType type)
        {
            switch (type)
            {
                case VFXValueType.kFloat:
                case VFXValueType.kFloat2:
                case VFXValueType.kFloat3:
                case VFXValueType.kFloat4:
                case VFXValueType.kInt:
                case VFXValueType.kUint:
                case VFXValueType.kTexture2D:
                case VFXValueType.kTexture3D:
                case VFXValueType.kTransform:
                    return true;
            }

            return false;
        }

        protected VFXExpression(Flags flags, params VFXExpression[] parents)
        {
            m_Parents = parents;
            m_Flags = flags;
            PropagateParentsFlags();
        }

        //Helper using reflection to recreate a concrete type from an abstract class (useful with reduce behavior)
        protected static VFXExpression CreateNewInstance(Type expressionType)
        {
            var allconstructors = expressionType.GetConstructors().ToArray();
            if (allconstructors.Length == 0)
                return null; //Only static readonly expression allowed, constructors are private (attribute or builtIn)

            var constructor =   allconstructors
                .OrderBy(o => o.GetParameters().Count())                 //promote simplest (or default) constructors
                .First();
            var param = constructor.GetParameters().Select(o =>
                {
                    var type = o.GetType();
                    return type.IsValueType ? Activator.CreateInstance(type) : null;
                }).ToArray();
            return (VFXExpression)constructor.Invoke(param);
        }

        protected VFXExpression CreateNewInstance()
        {
            return CreateNewInstance(GetType());
        }

        protected static T[] CollectStaticReadOnlyExpression<T>(Type expressionType) where T : VFXExpression
        {
            var members = expressionType.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Where(m => m.IsInitOnly && m.FieldType == typeof(T))
                .ToArray();
            var expressions = members.Select(m => m.GetValue(null) as T).ToArray();
            return expressions;
        }

        // Reduce the expression within a given context
        protected abstract VFXExpression Reduce(VFXExpression[] reducedParents);
        protected abstract VFXExpression Evaluate(VFXExpression[] constParents);
        public abstract string GetCodeString(string[] parents);

        public virtual IEnumerable<VFXAttributeInfo> GetNeededAttributes()
        {
            return Enumerable.Empty<VFXAttributeInfo>();
        }

        public bool Is(Flags flag)      { return (m_Flags & flag) == flag; }
        public bool IsAny(Flags flag)   { return (m_Flags & flag) != 0; }

        public abstract VFXValueType ValueType { get; }
        public abstract VFXExpressionOp Operation { get; }

        public VFXExpression[] Parents { get { return m_Parents; } }

        public override bool Equals(object obj)
        {
            var other = obj as VFXExpression;
            if (other == null)
                return false;

            if (Operation != other.Operation)
                return false;

            if (ValueType != other.ValueType)
                return false;

            if (m_Flags != other.m_Flags)
                return false;

            var addionnalParams = AdditionnalParameters;
            var otherAdditionnalParams = other.AdditionnalParameters;

            if (addionnalParams.Length != otherAdditionnalParams.Length)
                return false;

            for (int i = 0; i < addionnalParams.Length; ++i)
                if (addionnalParams[i] != otherAdditionnalParams[i])
                    return false;

            //if (GetHashCode() != obj.GetHashCode())
            //    return false;

            // TODO Not really optimized for an equal function!
            var thisParents = Parents;
            var otherParents = other.Parents;

            if (thisParents == null && otherParents == null)
                return true;
            if (thisParents == null || otherParents == null)
                return false;
            if (thisParents.Length != otherParents.Length)
                return false;

            for (int i = 0; i < thisParents.Length; ++i)
                if (!thisParents[i].Equals(otherParents[i]))
                    return false;

            return true;
        }

        public override int GetHashCode()
        {
            int hash = GetType().GetHashCode();

            var parents = Parents;
            for (int i = 0; i < parents.Length; ++i)
                hash = (hash * 397) ^ parents[i].GetHashCode(); // 397 taken from resharper

            var addionnalParameters = AdditionnalParameters;
            for (int i = 0; i < addionnalParameters.Length; ++i)
                hash = (hash * 397) ^ addionnalParameters[i].GetHashCode();

            hash = (hash * 397) ^ m_Flags.GetHashCode();
            hash = (hash * 397) ^ ValueType.GetHashCode();
            hash = (hash * 397) ^ Operation.GetHashCode();

            return hash;
        }

        public virtual int[] AdditionnalParameters { get { return new int[] {}; } }
        public virtual T Get<T>()
        {
            var value = (this as VFXValue<T>);
            if (value == null)
            {
                throw new ArgumentException(string.Format("Get isn't available for {0} with {1}", typeof(T).FullName, GetType().FullName));
            }
            return value.Get();
        }

        public virtual object GetContent()
        {
            throw new ArgumentException(string.Format("GetContent isn't available for {0}", GetType().FullName));
        }

        // Only do that when constructing an instance if needed
        protected void Initialize(Flags additionalFlags, VFXExpression[] parents)
        {
            m_Parents = parents;
            m_Flags |= additionalFlags;
            PropagateParentsFlags();
        }

        private void PropagateParentsFlags()
        {
            foreach (var parent in m_Parents)
            {
                m_Flags |= (parent.m_Flags & (Flags.PerElement | Flags.InvalidOnCPU));
                if (parent.Is(Flags.PerElement | Flags.InvalidOnGPU))
                    m_Flags |= Flags.InvalidOnGPU; // Only propagate GPU validity for per element expressions
            }
        }

        protected Flags m_Flags = Flags.None;
        private VFXExpression[] m_Parents;
    }
}
