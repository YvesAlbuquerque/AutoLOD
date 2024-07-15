using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AutoLOD;
using Unity.AutoLOD.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.AutoLOD
{
    [Serializable]
    public class LODGroupHelper
    {
        public LODGroup lodGroup
        {
            get { return m_LODGroup; }
            set
            {
                m_LODGroup = value;
                m_LODs = null;
            }
        }

        public LOD[] lods
        {
            get
            {
                if (m_LODs == null && m_LODGroup)
                    m_LODs = m_LODGroup.GetLODs();

                return m_LODs;
            }
        }

        public Vector3 referencePoint
        {
            get
            {
                if (!m_ReferencePoint.HasValue)
                    m_ReferencePoint = m_LODGroup
                        ? m_LODGroup.transform.TransformPoint(m_LODGroup.localReferencePoint)
                        : Vector3.zero;

                return m_ReferencePoint.Value;
            }
        }

        public float worldSpaceSize
        {
            get
            {
                if (!m_WorldSpaceSize.HasValue && m_LODGroup)
                    m_WorldSpaceSize = m_LODGroup.GetWorldSpaceSize();

                return m_WorldSpaceSize ?? 0f;
            }

        }

        public int maxLOD
        {
            get
            {
                if (!m_MaxLOD.HasValue)
                    m_MaxLOD = lodGroup.GetMaxLOD();

                return m_MaxLOD.Value;
            }
        }

        [SerializeField] LODGroup m_LODGroup;

        //Transform m_Transform;
        LOD[] m_LODs;
        Vector3? m_ReferencePoint;
        float? m_WorldSpaceSize;
        int? m_MaxLOD;
    }

}