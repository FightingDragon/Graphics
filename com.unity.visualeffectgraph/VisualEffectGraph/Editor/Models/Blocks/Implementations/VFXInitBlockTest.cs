using System;
using UnityEngine;

namespace UnityEditor.VFX
{
    [VFXInfo(category = "Tests")]
    class VFXInitBlockTest : VFXBlock
    {
        public override string name                         { get { return "Init Block"; }}
        public override VFXContextType compatibleContexts   { get { return VFXContextType.kInit; } }
        public override VFXDataType compatibleData          { get { return VFXDataType.kParticle; } }
    }
}
