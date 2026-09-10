using System;

namespace BoscaliSummer.Framework.Features
{
    internal interface IModFeature
    {
        FeatureMetadata Metadata { get; }
        Type[] PatchTypes { get; }
        void Install(FeatureContext context);
    }
}
