using UnityEngine;

// RigidbodyTransform.cs
// 
// MIT License
//    
// Copyright(c) 2018 Unity Technologies
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

/// <summary>
/// バネトルクサンプル
/// 詳しくはUTJの安原様の講演にて
/// https://www.slideshare.net/UnityTechnologiesJapan/unite-2018-tokyo
/// </summary>
public class SpringTorque : MonoBehaviour {
    public Transform target;        // 目標
    public float torqueLevel = 10f; // 旋回力（バネ係数）
    public float damper = 4f;       // 旋回減衰力（弱すぎるとブレる、強すぎると動きが重い）

    private Vector3 omega;    // 角速度


    void Update() {
        // バネトルク
        var targetpos = target.position;
        var diff = targetpos - transform.position;
        var up = transform.TransformVector(Vector3.up);
        var targetrot = Quaternion.LookRotation(diff, up);
        var myrot = transform.rotation;
        var rot = targetrot * Quaternion.Inverse(myrot);
        if (rot.w < 0f) {
            rot.x = -rot.x;
            rot.y = -rot.y;
            rot.z = -rot.z;
        }
        var torque = new Vector3(rot.x, rot.y, rot.z);

        // 角速度の更新
        var dt = Time.deltaTime;
        omega += torque * (torqueLevel * dt);
        omega -= omega * (damper * dt);
        // 回転値の更新
        var r = omega * dt;
        var w = Mathf.Sqrt(1f - r.sqrMagnitude);
        var q = new Quaternion(r.x, r.y, r.z, w);
        transform.rotation = q * myrot;
    }
}
