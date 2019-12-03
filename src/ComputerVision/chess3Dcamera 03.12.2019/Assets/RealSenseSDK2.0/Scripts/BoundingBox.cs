using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace chess3d
{
    public class BoundingBox : MonoBehaviour
    {
        public float width = 0.1f;
        public float height = 0.1f;
        public float depth = 0.0001f;

        public int lifeTime = 2;
        int lifeTimeCounter =0;
        public void Update()
        {
            if (lifeTimeCounter >= lifeTime)
            {
                Destroy(gameObject);
            }
            else
            {
                transform.localScale = new Vector3(height, width, depth);

                lifeTimeCounter++;
            }
        }

       

    }
}
