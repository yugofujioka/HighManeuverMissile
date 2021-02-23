using UnityEngine;

#if DRAW_MESH || UNITY_STANDALONE_OSX
// MainThread負荷が大きいのでせめてBurstする（本気でこの方針で組むならJobSystemにする）
[Unity.Burst.BurstCompile]
public static class BurstGeometry {
    public const int rows = 8, cols = 8; // 角形
    //public const float rows_inv = Mathf.PI * 2.0f / (rows + 1);
    public const float cols_inv = Mathf.PI * 2.0f / cols; // 一角当たりのラジアン
    public const int quadVtx = 6; // 矩形描画に必要なインデックスは6つ

    /// <summary>
    /// インデックスバッファーの生成
    /// </summary>
    /// <param name="trailNum">トレイル最大数</param>
    /// <param name="nodeNum">トレイル1つのノード数</param>
    /// <param name="indicies">インデックスバッファー</param>
    [Unity.Burst.BurstCompile]
    public unsafe static void CreateIndexBuffer(int trailNum, int nodeNum, int* indicies) {
    //public unsafe static void CreateIndexBuffer(int trailNum, int nodeNum, Unity.Collections.NativeArray<int> indicies) { // Debug用
        for (var trailIdx = 0; trailIdx < trailNum; ++trailIdx) {
            var startNodeIdx = trailIdx * (nodeNum - 1);
            // 末尾ノードを描画しないので nodeNum - 1
            for (var nodeIdx = 0; nodeIdx < (nodeNum - 1); ++nodeIdx) {
                //var idx = (startNodeIdx + nodeIdx) * quadVtx * cols;
                var vertIdx = trailIdx * nodeNum * cols + nodeIdx * cols;
                for (int c = 0; c < cols; ++c) {
                    var next = c == (cols - 1) ? -c : 1;
                    var idx = (startNodeIdx + nodeIdx) * quadVtx * cols;
                    idx += (c * quadVtx);
                    indicies[idx + 0] = vertIdx + c;
                    indicies[idx + 1] = vertIdx + c + cols;
                    indicies[idx + 2] = vertIdx + c + next;
                    indicies[idx + 3] = vertIdx + c + next;
                    indicies[idx + 4] = vertIdx + c + cols;
                    indicies[idx + 5] = vertIdx + c + cols + next;
                }
            }
            // 頂点演算重いので尻の移植はオミット
            //// 頭を尻に接続
            //var lastNode = trailIdx * this.nodeNum + this.nodeNum - 1;
            //for (int c = 0; c < cols; ++c) {
            //    this.indicies[lastNode * cols * 6 + c * 6 + 0] = lastNode * cols + c;
            //    this.indicies[lastNode * cols * 6 + c * 6 + 1] = c;
            //    this.indicies[lastNode * cols * 6 + c * 6 + 2] = lastNode * cols + c + 1;
            //    this.indicies[lastNode * cols * 6 + c * 6 + 3] = lastNode * cols + c + 1;
            //    this.indicies[lastNode * cols * 6 + c * 6 + 4] = c;
            //    this.indicies[lastNode * cols * 6 + c * 6 + 5] = c + 1;
            //}
        }
    }

    // 各トレイルのノードは1つのバッファで管理されてループするので1トレイル分のノード番号から全体のノード番号を算出
    static int ToNodeBufIdx(int trailIdx, int nodeIdx, int nodeNum) {
        nodeIdx %= nodeNum;
        return trailIdx * nodeNum + nodeIdx;
    }

    static bool IsValid(in ManeuverTrail.Node node, in float time, in float life) {
        return node.time >= 0f && (time - node.time) < life;
    }

    [Unity.Burst.BurstCompile]
    public unsafe static int UpdateGeometry(int trailNum, int nodeNum,
                                float thickness,
                                float time,
                                float life,
                                ManeuverTrail.Trail* trailArray,
                                ManeuverTrail.Node* nodeArray,
                                Vector3* vertices) {
    // Debug用
    //public static void UpdateGeometry(int trailNum, int nodeNum,
    //                            float thickness,
    //                            float time,
    //                            float life,
    //                            Unity.Collections.NativeArray<ManeuverTrail.Trail> trailArray,
    //                            Unity.Collections.NativeArray<ManeuverTrail.Node> nodeArray,
    //                            Unity.Collections.NativeArray<Vector3> vertices) {
    
        var activeTrail = 0;
        var vertIdx = 0;
        for (int trailIdx = 0; trailIdx < trailNum; ++trailIdx) {
            var currentNodeIdx = trailArray[trailIdx].currentNodeIdx;
            // 未実行
            if (currentNodeIdx < 0)
                continue;

            ++activeTrail;
            for (int nodeIdx = 0; nodeIdx < nodeNum; ++nodeIdx) {
                var idx = ToNodeBufIdx(trailIdx, currentNodeIdx + nodeIdx, nodeNum);
                var node1 = nodeArray[idx];
                var nextIdx = ToNodeBufIdx(trailIdx, currentNodeIdx + nodeIdx + 1, nodeNum);
                var node2 = nodeArray[nextIdx];

                bool isValid = node2.time >= node1.time;
                isValid &= IsValid(node1, time, life);
                if (!isValid)
                    node1 = node2;

                Vector3 p0 = node1.position;
                Vector3 p1 = node2.position;

                Vector3 t = Vector3.Normalize(p1 - p0); // 進行方向
                Vector3 bn = Vector3.Cross(t, new Vector3(0, 1, 0));
                //Vector3 bn = cross(t, IN[0].viewDir); // 上ベクトル
                Vector3 n = Vector3.Cross(t, bn);       // 横ベクトル

                // side
                for (int i = 0; i < cols; i++) {
                    var r = (float)i * cols_inv;
                    var normal = Vector3.Normalize(n * Mathf.Cos(r) + bn * Mathf.Sin(r));
                    vertices[vertIdx] = p0 + normal * thickness;
                    vertIdx++;
                }
            }
        }
        return activeTrail;
    }
}
#endif