using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace chess3d
{

    public class Pawn : MonoBehaviour

    {
 
 

        public int lifeTime = 5;
        int lifeTimeCounter = 0;
        public void Update()
        {
            if (lifeTimeCounter >= lifeTime)
            {
                Destroy(gameObject);
            }
            else
            {
                transform.localScale = new Vector3();

                lifeTimeCounter++;
            }
        }
    }

}
