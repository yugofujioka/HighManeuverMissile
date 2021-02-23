
// 参考 : @fuqunaga Unity Graphics Programming vol.2 GPU Trail
// https://indievisuallab.stores.jp/items/5ae077b850bbc30f3a000a6d

#ifndef MTRAILS_INCLUDED
#define MTRAILS_INCLUDED

// トレイル毎の入力情報
struct Input {
    int enable;
    float3 position;
    float3 direct;
    float speed;
};

// 各トレイルの現在の先頭ノード番号
struct Trail {
    int currentNodeIdx;
};

// ノード情報
struct Node {
    float time;
    float3 position;
    float3 direct;
    float speed;
};

uint _NodeNumPerTrail;
uint _TrailNum;
float _CheckTime;
float _Life;

// 各トレイルのノードは1つのバッファで管理されているので1トレイル分のノード番号から全体のノード番号を算出
int ToNodeBufIdx(int trailIdx, int nodeIdx) {
    nodeIdx %= _NodeNumPerTrail;
    return trailIdx * _NodeNumPerTrail + nodeIdx;
}

// 生存ノードか
bool IsValid(Node node) {
    return node.time >= 0 && (_CheckTime - node.time) < _Life;
}
#endif