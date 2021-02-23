using UnityEngine;
using System.Collections.Generic;


/// <summary>
/// ミサイルパラメータ
/// </summary>
[System.Serializable]
public struct MissileParam {
    public float startSpeed;  // 初速（発射直後の速度）
    public float maxSpeed;    // 最大速度（追従中の最大速度、非旋回中に加速がかかるので最大閾値）
    public float accelTime;   // 加速時間（小さいほど急加速する）
    
    public float torqueLevel; // 旋回力（バネ係数）
    public float damper;      // 旋回減衰力（弱すぎるとブレる、強すぎると動きが重い）
    
    public float shootDirect;     // 発射方向遷移値
    public Vector2 shoot;         // 発射力
    public Vector2Int shootFrame; // 発射力有効時間フレーム(x～yのランダム)
    public Vector2 power;         // 急制動力
    public Vector2Int powerFrame; // 急制動力有効時間フレーム(x～yのランダム)
}

public enum MISSILE {
    NORMAL, // 通常
    GOOD,   // 命中率高い（旋回角速度が高い）
    MOVE,   // 見せ（直進時に急制動）
}


/// <summary>
/// ミサイルポッド
/// </summary>
public class MissilePod : MonoBehaviour {
    public bool front = true;
    public int shootMax = 16;          // 射撃数（※トレイルバッファ数以上は出ない）
    public float frontPower = 1f;
    public ManeuverTrail trail = null;  // トレイルクラス
    public Transform[] target = null;  // 攻撃対象
    public Mesh missileMesh = null;    // ミサイル用メッシュ
    public Material missileMat = null; // ミサイル用マテリアル
    public MissileParam param;         // ミサイルパラメータ
    
    private bool shooting = false;
    private int shootWait = 0;
    private int shootCount = 0;
    private ManeuverMissle[] missile = null;
    private List<Matrix4x4> matrixList = null;

    private List<Vector3> directs = new List<Vector3>();

    void Start() {
        this.missile = new ManeuverMissle[this.trail.trailNum];
        this.matrixList = new List<Matrix4x4>(this.trail.trailNum);

        for (int i = 0; i < this.missile.Length; ++i)
            this.missile[i].Initialize(i, this.trail);
    }

    void Update() {
            if (Input.GetKeyDown(KeyCode.P)) {
                ScreenCapture.CaptureScreenshot("Capture.png");
            }
        // スペースキーでリセット
        if (Input.GetKeyDown(KeyCode.Space)) {
            for (int i = 0; i < this.missile.Length; ++i)
                this.missile[i].Vanish();
            this.trail.Clear();
        }
        
        float elapsedTime = Time.deltaTime;

        // ミサイルの更新
        this.matrixList.Clear();
        for (int i = 0; i < this.missile.Length; ++i) {
            if (!this.missile[i].active)
                continue;
            this.missile[i].Proc(elapsedTime);
            this.matrixList.Add(this.missile[i].matrix);
        }

        // ミサイル発射
        if (this.shooting) {
            if (--this.shootWait < 1) {
                //Vector3 dir = Random.onUnitSphere;
                //if (front)
                //    dir.z = Mathf.Max(Mathf.Abs(dir.z), 0.4f);
                //else
                //    dir.z = Mathf.Min(Mathf.Abs(dir.z), -0.4f);
                //dir.Normalize();
                var dir = this.directs[this.shootCount];
                if (this.front)
                    dir.z = -dir.z;
                Vector3 position = this.transform.position;
                int targetId = this.shootCount % this.target.Length;
                this.missile[this.shootCount].Shoot(position, dir, this.target[targetId], this.param);

                // 発射間隔ばらす
                this.shootWait = Random.Range(0, 2);

                if (++this.shootCount >= this.shootMax)
                    this.shooting = false;
            }
        } else {
            // マウス左クリックで発射
            if (Input.GetMouseButtonDown(0)) {
                // 強制クリア
                for (int i = 0; i < this.missile.Length; ++i)
                    this.missile[i].Vanish();
                this.trail.Clear();

                this.shooting = true;
                this.shootCount = 0;
                this.shootMax %= (this.trail.trailNum + 1);

                if (this.shootMax != this.directs.Count) {
                    this.directs.Clear();
                    var startAngle = Random.Range(0f, 360f);
                    var deltaAngle = 360f / this.shootMax;
                    for (int i = 0; i < this.shootMax; ++i) {
                        var angle = deltaAngle * i;
                        angle += Random.Range(-deltaAngle * 0.45f, deltaAngle * 0.45f);
                        angle += startAngle;
                        var direct = Quaternion.AngleAxis(angle, Vector3.forward) * Vector3.up;
                        direct.z = -this.frontPower;
                        this.directs.Add(direct);
                    }
                }
                ShuffleDirects();
            }
        }
        // ミサイル本体の描画
        Graphics.DrawMeshInstanced(this.missileMesh, 0, this.missileMat, this.matrixList, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
    }
    /// <summary>
    /// 発射順のシャッフル
    /// </summary>
    private void ShuffleDirects()  {
        int length = this.directs.Count;
        for (int i = 0; i < length; ++i) {
            var tmp = this.directs[i];
            int randomIndex = UnityEngine.Random.Range(i, length);
            this.directs[i] = this.directs[randomIndex];
            this.directs[randomIndex] = tmp;
        }
    }
}
