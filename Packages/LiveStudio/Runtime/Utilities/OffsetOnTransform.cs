
using System;
using UnityEngine;

namespace Lilium.LiveStudio
{
    [Serializable]
    public struct OffsetOnTransform
    {
        public Transform Transform;

        public Matrix4x4 OffsetRotation;

        private Matrix4x4 m_initialLocalMatrix;

        public Matrix4x4 WorldMatrix
        {
            get
            {
                if (Transform == null)
                {
                    return Matrix4x4.identity;
                }

                return Transform.localToWorldMatrix * OffsetRotation;
            }
        }

        public Vector3 WorldForward => WorldMatrix.GetColumn(2);

        public Matrix4x4 InitialWorldMatrix => Transform.parent.localToWorldMatrix * m_initialLocalMatrix;

        public void Setup()
        {
            if (!(Transform == null))
            {
                m_initialLocalMatrix = Transform.parent.worldToLocalMatrix * Transform.localToWorldMatrix;
            }
        }

        public static OffsetOnTransform Create(Transform transform)
        {
            OffsetOnTransform result = new OffsetOnTransform
            {
                Transform = transform
            };
            if (transform != null)
            {
                result.OffsetRotation = RotationToWorldAxis(transform.worldToLocalMatrix);
            }

            return result;
        }


        public static Matrix4x4 RotationToWorldAxis(Matrix4x4 m)
        {
            return Matrix4x4FromColumns(m.MultiplyVector(Vector3.right), m.MultiplyVector(Vector3.up), m.MultiplyVector(Vector3.forward), new Vector4(0f, 0f, 0f, 1f));
        }

        public static Matrix4x4 Matrix4x4FromColumns(Vector4 c0, Vector4 c1, Vector4 c2, Vector4 c3)
        {
            return new Matrix4x4(c0, c1, c2, c3);
        }
    }

}