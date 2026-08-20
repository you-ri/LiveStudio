// Copyright (c) You-Ri, 2026

using Lilium.RemoteControl;
using UnityEngine;

// HumanBodyBones is a UnityEngine built-in enum, so it cannot carry [LiveEnum].
// Register it declaratively here so AvatarProp.targetBone shows a bone dropdown in the
// remote app. LastBone is the enum's count sentinel, not a real bone — exclude it.
[assembly: LiveExternalEnum(typeof(HumanBodyBones),
    excludeNames = new[] { nameof(HumanBodyBones.LastBone) })]

// LightShadows is a UnityEngine built-in enum as well. Light is exposed through the live class
// asset shipped with this package, so its shadows member needs the dropdown registered here.
[assembly: LiveExternalEnum(typeof(LightShadows))]
