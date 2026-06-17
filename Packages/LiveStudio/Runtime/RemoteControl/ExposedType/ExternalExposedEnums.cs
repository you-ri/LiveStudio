// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl;
using UnityEngine;

// HumanBodyBones is a UnityEngine built-in enum, so it cannot carry [ExposedEnum].
// Register it declaratively here so AvatarProp.targetBone shows a bone dropdown in the
// remote app. LastBone is the enum's count sentinel, not a real bone — exclude it.
[assembly: ExposedExternalEnum(typeof(HumanBodyBones),
    excludeNames = new[] { nameof(HumanBodyBones.LastBone) })]
