// Copyright (c) You-Ri, 2026

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Names for the 95 humanoid muscles, in the order <see cref="UnityEngine.HumanPose.muscles"/>
    /// uses -- which is also the order <see cref="UnityEngine.Animations.MuscleHandle"/> comes back in
    /// (measured; the two APIs spell the finger muscles differently but agree on the order).
    ///
    /// Unity only publishes these as a string table (<see cref="UnityEngine.HumanTrait.MuscleName"/>),
    /// and a string table cannot label an array on the live data. Without this, a recorded pose reads
    /// as 95 numbered floats and finding "the left elbow" means counting. The identifiers drop the
    /// spaces and hyphens from Unity's spelling; the doc comment on each one keeps the original.
    ///
    /// Kept honest by HumanoidMuscleStreamTests: a Unity release that reorders or renames the table
    /// fails there rather than silently landing values on the wrong joint.
    /// </summary>
    public enum HumanMuscle
    {
        /// <summary>"Spine Front-Back"</summary>
        SpineFrontBack = 0,

        /// <summary>"Spine Left-Right"</summary>
        SpineLeftRight = 1,

        /// <summary>"Spine Twist Left-Right"</summary>
        SpineTwistLeftRight = 2,

        /// <summary>"Chest Front-Back"</summary>
        ChestFrontBack = 3,

        /// <summary>"Chest Left-Right"</summary>
        ChestLeftRight = 4,

        /// <summary>"Chest Twist Left-Right"</summary>
        ChestTwistLeftRight = 5,

        /// <summary>"UpperChest Front-Back"</summary>
        UpperChestFrontBack = 6,

        /// <summary>"UpperChest Left-Right"</summary>
        UpperChestLeftRight = 7,

        /// <summary>"UpperChest Twist Left-Right"</summary>
        UpperChestTwistLeftRight = 8,

        /// <summary>"Neck Nod Down-Up"</summary>
        NeckNodDownUp = 9,

        /// <summary>"Neck Tilt Left-Right"</summary>
        NeckTiltLeftRight = 10,

        /// <summary>"Neck Turn Left-Right"</summary>
        NeckTurnLeftRight = 11,

        /// <summary>"Head Nod Down-Up"</summary>
        HeadNodDownUp = 12,

        /// <summary>"Head Tilt Left-Right"</summary>
        HeadTiltLeftRight = 13,

        /// <summary>"Head Turn Left-Right"</summary>
        HeadTurnLeftRight = 14,

        /// <summary>"Left Eye Down-Up"</summary>
        LeftEyeDownUp = 15,

        /// <summary>"Left Eye In-Out"</summary>
        LeftEyeInOut = 16,

        /// <summary>"Right Eye Down-Up"</summary>
        RightEyeDownUp = 17,

        /// <summary>"Right Eye In-Out"</summary>
        RightEyeInOut = 18,

        /// <summary>"Jaw Close"</summary>
        JawClose = 19,

        /// <summary>"Jaw Left-Right"</summary>
        JawLeftRight = 20,

        /// <summary>"Left Upper Leg Front-Back"</summary>
        LeftUpperLegFrontBack = 21,

        /// <summary>"Left Upper Leg In-Out"</summary>
        LeftUpperLegInOut = 22,

        /// <summary>"Left Upper Leg Twist In-Out"</summary>
        LeftUpperLegTwistInOut = 23,

        /// <summary>"Left Lower Leg Stretch"</summary>
        LeftLowerLegStretch = 24,

        /// <summary>"Left Lower Leg Twist In-Out"</summary>
        LeftLowerLegTwistInOut = 25,

        /// <summary>"Left Foot Up-Down"</summary>
        LeftFootUpDown = 26,

        /// <summary>"Left Foot Twist In-Out"</summary>
        LeftFootTwistInOut = 27,

        /// <summary>"Left Toes Up-Down"</summary>
        LeftToesUpDown = 28,

        /// <summary>"Right Upper Leg Front-Back"</summary>
        RightUpperLegFrontBack = 29,

        /// <summary>"Right Upper Leg In-Out"</summary>
        RightUpperLegInOut = 30,

        /// <summary>"Right Upper Leg Twist In-Out"</summary>
        RightUpperLegTwistInOut = 31,

        /// <summary>"Right Lower Leg Stretch"</summary>
        RightLowerLegStretch = 32,

        /// <summary>"Right Lower Leg Twist In-Out"</summary>
        RightLowerLegTwistInOut = 33,

        /// <summary>"Right Foot Up-Down"</summary>
        RightFootUpDown = 34,

        /// <summary>"Right Foot Twist In-Out"</summary>
        RightFootTwistInOut = 35,

        /// <summary>"Right Toes Up-Down"</summary>
        RightToesUpDown = 36,

        /// <summary>"Left Shoulder Down-Up"</summary>
        LeftShoulderDownUp = 37,

        /// <summary>"Left Shoulder Front-Back"</summary>
        LeftShoulderFrontBack = 38,

        /// <summary>"Left Arm Down-Up"</summary>
        LeftArmDownUp = 39,

        /// <summary>"Left Arm Front-Back"</summary>
        LeftArmFrontBack = 40,

        /// <summary>"Left Arm Twist In-Out"</summary>
        LeftArmTwistInOut = 41,

        /// <summary>"Left Forearm Stretch"</summary>
        LeftForearmStretch = 42,

        /// <summary>"Left Forearm Twist In-Out"</summary>
        LeftForearmTwistInOut = 43,

        /// <summary>"Left Hand Down-Up"</summary>
        LeftHandDownUp = 44,

        /// <summary>"Left Hand In-Out"</summary>
        LeftHandInOut = 45,

        /// <summary>"Right Shoulder Down-Up"</summary>
        RightShoulderDownUp = 46,

        /// <summary>"Right Shoulder Front-Back"</summary>
        RightShoulderFrontBack = 47,

        /// <summary>"Right Arm Down-Up"</summary>
        RightArmDownUp = 48,

        /// <summary>"Right Arm Front-Back"</summary>
        RightArmFrontBack = 49,

        /// <summary>"Right Arm Twist In-Out"</summary>
        RightArmTwistInOut = 50,

        /// <summary>"Right Forearm Stretch"</summary>
        RightForearmStretch = 51,

        /// <summary>"Right Forearm Twist In-Out"</summary>
        RightForearmTwistInOut = 52,

        /// <summary>"Right Hand Down-Up"</summary>
        RightHandDownUp = 53,

        /// <summary>"Right Hand In-Out"</summary>
        RightHandInOut = 54,

        /// <summary>"Left Thumb 1 Stretched"</summary>
        LeftThumb1Stretched = 55,

        /// <summary>"Left Thumb Spread"</summary>
        LeftThumbSpread = 56,

        /// <summary>"Left Thumb 2 Stretched"</summary>
        LeftThumb2Stretched = 57,

        /// <summary>"Left Thumb 3 Stretched"</summary>
        LeftThumb3Stretched = 58,

        /// <summary>"Left Index 1 Stretched"</summary>
        LeftIndex1Stretched = 59,

        /// <summary>"Left Index Spread"</summary>
        LeftIndexSpread = 60,

        /// <summary>"Left Index 2 Stretched"</summary>
        LeftIndex2Stretched = 61,

        /// <summary>"Left Index 3 Stretched"</summary>
        LeftIndex3Stretched = 62,

        /// <summary>"Left Middle 1 Stretched"</summary>
        LeftMiddle1Stretched = 63,

        /// <summary>"Left Middle Spread"</summary>
        LeftMiddleSpread = 64,

        /// <summary>"Left Middle 2 Stretched"</summary>
        LeftMiddle2Stretched = 65,

        /// <summary>"Left Middle 3 Stretched"</summary>
        LeftMiddle3Stretched = 66,

        /// <summary>"Left Ring 1 Stretched"</summary>
        LeftRing1Stretched = 67,

        /// <summary>"Left Ring Spread"</summary>
        LeftRingSpread = 68,

        /// <summary>"Left Ring 2 Stretched"</summary>
        LeftRing2Stretched = 69,

        /// <summary>"Left Ring 3 Stretched"</summary>
        LeftRing3Stretched = 70,

        /// <summary>"Left Little 1 Stretched"</summary>
        LeftLittle1Stretched = 71,

        /// <summary>"Left Little Spread"</summary>
        LeftLittleSpread = 72,

        /// <summary>"Left Little 2 Stretched"</summary>
        LeftLittle2Stretched = 73,

        /// <summary>"Left Little 3 Stretched"</summary>
        LeftLittle3Stretched = 74,

        /// <summary>"Right Thumb 1 Stretched"</summary>
        RightThumb1Stretched = 75,

        /// <summary>"Right Thumb Spread"</summary>
        RightThumbSpread = 76,

        /// <summary>"Right Thumb 2 Stretched"</summary>
        RightThumb2Stretched = 77,

        /// <summary>"Right Thumb 3 Stretched"</summary>
        RightThumb3Stretched = 78,

        /// <summary>"Right Index 1 Stretched"</summary>
        RightIndex1Stretched = 79,

        /// <summary>"Right Index Spread"</summary>
        RightIndexSpread = 80,

        /// <summary>"Right Index 2 Stretched"</summary>
        RightIndex2Stretched = 81,

        /// <summary>"Right Index 3 Stretched"</summary>
        RightIndex3Stretched = 82,

        /// <summary>"Right Middle 1 Stretched"</summary>
        RightMiddle1Stretched = 83,

        /// <summary>"Right Middle Spread"</summary>
        RightMiddleSpread = 84,

        /// <summary>"Right Middle 2 Stretched"</summary>
        RightMiddle2Stretched = 85,

        /// <summary>"Right Middle 3 Stretched"</summary>
        RightMiddle3Stretched = 86,

        /// <summary>"Right Ring 1 Stretched"</summary>
        RightRing1Stretched = 87,

        /// <summary>"Right Ring Spread"</summary>
        RightRingSpread = 88,

        /// <summary>"Right Ring 2 Stretched"</summary>
        RightRing2Stretched = 89,

        /// <summary>"Right Ring 3 Stretched"</summary>
        RightRing3Stretched = 90,

        /// <summary>"Right Little 1 Stretched"</summary>
        RightLittle1Stretched = 91,

        /// <summary>"Right Little Spread"</summary>
        RightLittleSpread = 92,

        /// <summary>"Right Little 2 Stretched"</summary>
        RightLittle2Stretched = 93,

        /// <summary>"Right Little 3 Stretched"</summary>
        RightLittle3Stretched = 94,
    }
}
