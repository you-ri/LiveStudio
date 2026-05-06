// Copyright (c) You-Ri, 2026

namespace Lilium.LiveStudio
{
    public enum ExpressionMode
    {
        Preset,
        PerfectSync,
    }

    public interface IAvatar : IExpressionAvatar
    {
        public void BuildAvatar();

        public void SetExpressionConfig(AvatarExpressionConfig config);

        public void ResetPhysics();

        public void SetMotionSource(MotionSourceBase motionSource);
    }
}
