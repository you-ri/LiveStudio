using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Lilium.RemoteControl;

namespace Lilium.LiveStudio
{
    /// <summary>
    /// Enum values that identify the face action units affecting the expression on the face.
    /// </summary>
    /// <remarks>Each action unit corresponds to a facial feature that can move. A coefficient of zero for the
    /// feature represents the neutral position, while a coefficient of one represents the fully articulated
    /// position.
    ///
    /// Call <see cref="ARKitFaceSubsystem.GetBlendShapeCoefficients*"/> to get the current coefficients of each
    /// blend shape location for the current facial expression.
    /// </remarks>
    [ExposedEnum]
    public enum ARKitBlendShapeLocation
    {
        /// <summary>
        /// The coefficient describing downward movement of the outer portion of the left eyebrow.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationbrowdownleft).
        /// </summary>
        BrowDownLeft,

        /// <summary>
        /// The coefficient describing downward movement of the outer portion of the right eyebrow.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationbrowdownright).
        /// </summary>
        BrowDownRight,

        /// <summary>
        /// The coefficient describing upward movement of the inner portion of both eyebrows.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationbrowinnerup).
        /// </summary>
        BrowInnerUp,

        /// <summary>
        /// The coefficient describing upward movement of the outer portion of the left eyebrow.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationbrowouterupleft).
        /// </summary>
        BrowOuterUpLeft,

        /// <summary>
        /// The coefficient describing upward movement of the outer portion of the right eyebrow.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationbrowouterupright).
        /// </summary>
        BrowOuterUpRight,

        /// <summary>
        /// The coefficient describing outward movement of both cheeks.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationcheekpuff).
        /// </summary>
        CheekPuff,

        /// <summary>
        /// The coefficient describing upward movement of the cheek around and below the left eye.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationcheeksquintleft).
        /// </summary>
        CheekSquintLeft,

        /// <summary>
        /// The coefficient describing upward movement of the cheek around and below the right eye.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationcheeksquintright).
        /// </summary>
        CheekSquintRight,

        /// <summary>
        /// The coefficient describing closure of the eyelids over the left eye.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyeblinkleft).
        /// </summary>
        EyeBlinkLeft,

        /// <summary>
        /// The coefficient describing closure of the eyelids over the right eye.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyeblinkright).
        /// </summary>
        EyeBlinkRight,

        /// <summary>
        /// The coefficient describing movement of the left eyelids consistent with a downward gaze.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyelookdownleft).
        /// </summary>
        EyeLookDownLeft,

        /// <summary>
        /// The coefficient describing movement of the right eyelids consistent with a downward gaze.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyelookdownright).
        /// </summary>
        EyeLookDownRight,

        /// <summary>
        /// The coefficient describing movement of the left eyelids consistent with a rightward gaze.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyelookinleft).
        /// </summary>
        EyeLookInLeft,

        /// <summary>
        /// The coefficient describing movement of the right eyelids consistent with a leftward gaze.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyelookinright).
        /// </summary>
        EyeLookInRight,

        /// <summary>
        /// The coefficient describing movement of the left eyelids consistent with a leftward gaze.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyelookoutleft).
        /// </summary>
        EyeLookOutLeft,

        /// <summary>
        /// The coefficient describing movement of the right eyelids consistent with a rightward gaze.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyelookoutright).
        /// </summary>
        EyeLookOutRight,

        /// <summary>
        /// The coefficient describing movement of the left eyelids consistent with an upward gaze.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyelookupleft).
        /// </summary>
        EyeLookUpLeft,

        /// <summary>
        /// The coefficient describing movement of the right eyelids consistent with an upward gaze.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyelookupright).
        /// </summary>
        EyeLookUpRight,

        /// <summary>
        /// The coefficient describing contraction of the face around the left eye.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyesquintleft).
        /// </summary>
        EyeSquintLeft,

        /// <summary>
        /// The coefficient describing contraction of the face around the right eye.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyesquintright).
        /// </summary>
        EyeSquintRight,

        /// <summary>
        /// The coefficient describing a widening of the eyelids around the left eye.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyewideleft).
        /// </summary>
        EyeWideLeft,

        /// <summary>
        /// The coefficient describing a widening of the eyelids around the right eye.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationeyewideright).
        /// </summary>
        EyeWideRight,

        /// <summary>
        /// The coefficient describing forward movement of the lower jaw.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationjawforward).
        /// </summary>
        JawForward,

        /// <summary>
        /// The coefficient describing leftward movement of the lower jaw.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationjawleft).
        /// </summary>
        JawLeft,

        /// <summary>
        /// The coefficient describing an opening of the lower jaw.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationjawopen).
        /// </summary>
        JawOpen,

        /// <summary>
        /// The coefficient describing rightward movement of the lower jaw.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationjawright).
        /// </summary>
        JawRight,

        /// <summary>
        /// The coefficient describing closure of the lips independent of jaw position.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthclose).
        /// </summary>
        MouthClose,

        /// <summary>
        /// The coefficient describing backward movement of the left corner of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthdimpleleft).
        /// </summary>
        MouthDimpleLeft,

        /// <summary>
        /// The coefficient describing backward movement of the right corner of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthdimpleright).
        /// </summary>
        MouthDimpleRight,

        /// <summary>
        /// The coefficient describing downward movement of the left corner of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthfrownleft).
        /// </summary>
        MouthFrownLeft,

        /// <summary>
        /// The coefficient describing downward movement of the right corner of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthfrownright).
        /// </summary>
        MouthFrownRight,

        /// <summary>
        /// The coefficient describing contraction of both lips into an open shape.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthfunnel).
        /// </summary>
        MouthFunnel,

        /// <summary>
        /// The coefficient describing leftward movement of both lips together.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthleft).
        /// </summary>
        MouthLeft,

        /// <summary>
        /// The coefficient describing downward movement of the lower lip on the left side.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthlowerdownleft).
        /// </summary>
        MouthLowerDownLeft,

        /// <summary>
        /// The coefficient describing downward movement of the lower lip on the right side.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthlowerdownright).
        /// </summary>
        MouthLowerDownRight,

        /// <summary>
        /// The coefficient describing upward compression of the lower lip on the left side.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthpressleft).
        /// </summary>
        MouthPressLeft,

        /// <summary>
        /// The coefficient describing upward compression of the lower lip on the right side.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthpressright).
        /// </summary>
        MouthPressRight,

        /// <summary>
        /// The coefficient describing contraction and compression of both closed lips.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthpucker).
        /// </summary>
        MouthPucker,

        /// <summary>
        /// The coefficient describing rightward movement of both lips together.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthright).
        /// </summary>
        MouthRight,

        /// <summary>
        /// The coefficient describing movement of the lower lip toward the inside of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthrolllower).
        /// </summary>
        MouthRollLower,

        /// <summary>
        /// The coefficient describing movement of the upper lip toward the inside of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthrollupper).
        /// </summary>
        MouthRollUpper,

        /// <summary>
        /// The coefficient describing outward movement of the lower lip.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthshruglower).
        /// </summary>
        MouthShrugLower,

        /// <summary>
        /// The coefficient describing outward movement of the upper lip.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthshrugupper).
        /// </summary>
        MouthShrugUpper,

        /// <summary>
        /// The coefficient describing upward movement of the left corner of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthsmileleft).
        /// </summary>
        MouthSmileLeft,

        /// <summary>
        /// The coefficient describing upward movement of the right corner of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthsmileright).
        /// </summary>
        MouthSmileRight,

        /// <summary>
        /// The coefficient describing leftward movement of the left corner of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthstretchleft).
        /// </summary>
        MouthStretchLeft,

        /// <summary>
        /// The coefficient describing rightward movement of the left corner of the mouth.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthstretchright).
        /// </summary>
        MouthStretchRight,

        /// <summary>
        /// The coefficient describing upward movement of the upper lip on the left side.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthupperupleft).
        /// </summary>
        MouthUpperUpLeft,

        /// <summary>
        /// The coefficient describing upward movement of the upper lip on the right side.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationmouthupperupright).
        /// </summary>
        MouthUpperUpRight,

        /// <summary>
        /// The coefficient describing a raising of the left side of the nose around the nostril.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationnosesneerleft).
        /// </summary>
        NoseSneerLeft,

        /// <summary>
        /// The coefficient describing a raising of the right side of the nose around the nostril.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationnosesneerright).
        /// </summary>
        NoseSneerRight,

        /// <summary>
        /// The coefficient describing extension of the tongue.
        /// For more information, please refer to the
        /// [ARKit documentation](https://developer.apple.com/documentation/arkit/arblendshapelocationtongueout).
        /// </summary>
        TongueOut,

        Max,
    }

    public static partial class ARKitBlendShapeUtility
    {

        /// <summary>
        /// ARKitBlendShapeLocation から文字列への変換テーブル（ToString()のGC回避用）
        /// </summary>
        public static readonly Dictionary<ARKitBlendShapeLocation, string> ARKitToString = new Dictionary<ARKitBlendShapeLocation, string>
        {
            { ARKitBlendShapeLocation.BrowDownLeft, "BrowDownLeft" },
            { ARKitBlendShapeLocation.BrowDownRight, "BrowDownRight" },
            { ARKitBlendShapeLocation.BrowInnerUp, "BrowInnerUp" },
            { ARKitBlendShapeLocation.BrowOuterUpLeft, "BrowOuterUpLeft" },
            { ARKitBlendShapeLocation.BrowOuterUpRight, "BrowOuterUpRight" },
            { ARKitBlendShapeLocation.CheekPuff, "CheekPuff" },
            { ARKitBlendShapeLocation.CheekSquintLeft, "CheekSquintLeft" },
            { ARKitBlendShapeLocation.CheekSquintRight, "CheekSquintRight" },
            { ARKitBlendShapeLocation.EyeBlinkLeft, "EyeBlinkLeft" },
            { ARKitBlendShapeLocation.EyeBlinkRight, "EyeBlinkRight" },
            { ARKitBlendShapeLocation.EyeLookDownLeft, "EyeLookDownLeft" },
            { ARKitBlendShapeLocation.EyeLookDownRight, "EyeLookDownRight" },
            { ARKitBlendShapeLocation.EyeLookInLeft, "EyeLookInLeft" },
            { ARKitBlendShapeLocation.EyeLookInRight, "EyeLookInRight" },
            { ARKitBlendShapeLocation.EyeLookOutLeft, "EyeLookOutLeft" },
            { ARKitBlendShapeLocation.EyeLookOutRight, "EyeLookOutRight" },
            { ARKitBlendShapeLocation.EyeLookUpLeft, "EyeLookUpLeft" },
            { ARKitBlendShapeLocation.EyeLookUpRight, "EyeLookUpRight" },
            { ARKitBlendShapeLocation.EyeSquintLeft, "EyeSquintLeft" },
            { ARKitBlendShapeLocation.EyeSquintRight, "EyeSquintRight" },
            { ARKitBlendShapeLocation.EyeWideLeft, "EyeWideLeft" },
            { ARKitBlendShapeLocation.EyeWideRight, "EyeWideRight" },
            { ARKitBlendShapeLocation.JawForward, "JawForward" },
            { ARKitBlendShapeLocation.JawLeft, "JawLeft" },
            { ARKitBlendShapeLocation.JawOpen, "JawOpen" },
            { ARKitBlendShapeLocation.JawRight, "JawRight" },
            { ARKitBlendShapeLocation.MouthClose, "MouthClose" },
            { ARKitBlendShapeLocation.MouthDimpleLeft, "MouthDimpleLeft" },
            { ARKitBlendShapeLocation.MouthDimpleRight, "MouthDimpleRight" },
            { ARKitBlendShapeLocation.MouthFrownLeft, "MouthFrownLeft" },
            { ARKitBlendShapeLocation.MouthFrownRight, "MouthFrownRight" },
            { ARKitBlendShapeLocation.MouthFunnel, "MouthFunnel" },
            { ARKitBlendShapeLocation.MouthLeft, "MouthLeft" },
            { ARKitBlendShapeLocation.MouthLowerDownLeft, "MouthLowerDownLeft" },
            { ARKitBlendShapeLocation.MouthLowerDownRight, "MouthLowerDownRight" },
            { ARKitBlendShapeLocation.MouthPressLeft, "MouthPressLeft" },
            { ARKitBlendShapeLocation.MouthPressRight, "MouthPressRight" },
            { ARKitBlendShapeLocation.MouthPucker, "MouthPucker" },
            { ARKitBlendShapeLocation.MouthRight, "MouthRight" },
            { ARKitBlendShapeLocation.MouthRollLower, "MouthRollLower" },
            { ARKitBlendShapeLocation.MouthRollUpper, "MouthRollUpper" },
            { ARKitBlendShapeLocation.MouthShrugLower, "MouthShrugLower" },
            { ARKitBlendShapeLocation.MouthShrugUpper, "MouthShrugUpper" },
            { ARKitBlendShapeLocation.MouthSmileLeft, "MouthSmileLeft" },
            { ARKitBlendShapeLocation.MouthSmileRight, "MouthSmileRight" },
            { ARKitBlendShapeLocation.MouthStretchLeft, "MouthStretchLeft" },
            { ARKitBlendShapeLocation.MouthStretchRight, "MouthStretchRight" },
            { ARKitBlendShapeLocation.MouthUpperUpLeft, "MouthUpperUpLeft" },
            { ARKitBlendShapeLocation.MouthUpperUpRight, "MouthUpperUpRight" },
            { ARKitBlendShapeLocation.NoseSneerLeft, "NoseSneerLeft" },
            { ARKitBlendShapeLocation.NoseSneerRight, "NoseSneerRight" },
            { ARKitBlendShapeLocation.TongueOut, "TongueOut" }
        };
    }

}
