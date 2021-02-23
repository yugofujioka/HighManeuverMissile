using UnityEngine;


/// <summary>
/// トレイル付ミサイル
/// </summary>
public class TrailMissile : MonoBehaviour {
    #region DEFINE
    private const float HIT_RANGE = 1f; // 着弾範囲（適当）
    #endregion


    #region MEMBER
    public ManeuverTrail trail;  // トレイルエフェクト
    public Transform target;   // 目標
    public float startSpeed;   // 初速（発射直後の速度）
    public float maxSpeed;     // 最大速度（追従中の最大速度、非旋回中に加速がかかるので最大閾値）
    public float accelTime;    // 加速時間（小さいほど急加速する）
    public float torqueLevel;  // 旋回力（バネ係数）
    public float damper;       // 旋回減衰力（弱すぎるとブレる、強すぎると動きが重い）

    private Vector3 direct;    // 移動方向
    private float speed;       // 移動速度
    private Vector3 omega;     // 角速度
    private float shiftTime;   // 加速遷移時間
    #endregion


    #region MAIN FUNCTION
    /// <summary>
    /// 射撃
    /// </summary>
    void Start() {
        direct = transform.rotation * Vector3.forward;
        speed = startSpeed;
        omega = Vector3.zero;
        shiftTime = 0f;
        
        var move = direct * speed;
        trail.StartTrail(0, transform.position, move);
    }

    /// <summary>
    /// 派生クラスでの固有更新処理(falseを返すと消滅)
    /// </summary>
    void Update() {
        float elapsedTime = Time.deltaTime;

        // 移動
        if (ProcTorque(elapsedTime))
            return;

        // 終了
        gameObject.SetActive(false);
    }
    #endregion


    #region PRIVATE FUNCTION
    /// <summary>
    /// 追尾制御（バネトルク）
    /// </summary>
    /// <param name="elapsedTime">経過時間</param>
    /// <returns>有効か</returns>
    private bool ProcTorque(float elapsedTime) {
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
        var dt = elapsedTime;
        omega += torque * (torqueLevel * dt);
        omega -= omega * (damper * dt);
        // 回転値の更新
        var r = omega * dt;
        var w = Mathf.Sqrt(1f - r.sqrMagnitude);
        var q = new Quaternion(r.x, r.y, r.z, w);
        transform.rotation = q * myrot;

        // 進行方向の更新
        direct = transform.rotation * Vector3.forward;
        // 加速
        if (shiftTime < accelTime) {
            shiftTime += elapsedTime;
            float t = Mathf.Clamp01(shiftTime / accelTime);
            speed = EaseInQuad(startSpeed, maxSpeed, t);
        } else {
            speed = maxSpeed;
        }
        // 座標更新
        var move = direct * speed;
        var position = transform.position + move * elapsedTime;
        transform.position = position;
        // トレイル更新
        trail.UpdateTrail(0, position, move);
        // 接触判定（仮）
        if (Vector3.Distance(target.position, transform.position) < HIT_RANGE) {
            trail.StopTrail(0);
            return false;
        }
        return true;
    }

    /// <summary>
    /// イージング補間(2次)
    /// </summary>
    /// <param name="p">始点</param>
    /// <param name="q">終点</param>
    /// <param name="t">位置(0.0f～1.0f)</param>
    public float EaseInQuad(float p, float q, float t) {
        return (q - p) * t * t + p;
    }
    #endregion
}