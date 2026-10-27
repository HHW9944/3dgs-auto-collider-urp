# Changelog

## 0.1.0

PLY Gaussian 데이터로 Unity BoxCollider를 만드는 첫 배포입니다.

- `.ply`를 `SplatData`로 가져옵니다. 물리 좌표는 `(-x, -y, -z)`입니다.
- GPU Compute Shader로 복셀 밀도를 계산합니다.
- Legacy 모드는 밀도 임계값을 넘긴 복셀에 축 정렬 BoxCollider를 만들고, Auto Level을 사용할 수 있습니다.
- Surface 모드는 표면 복셀에서 PCA로 회전 BoxCollider를 만듭니다. Auto Level은 적용하지 않습니다.
- PCA에 실패한 표면 칸은 인접한 PCA 성공 칸의 평면으로 다시 맞춥니다. 검증에 실패하면 복셀 정육면체 Fallback을 유지합니다.
- 생성된 충돌체는 `CyanPca`, `GreenNeighbor`, `OrangeFallback` 아래로 나뉩니다.
- 선택한 오브젝트의 Gizmo는 PCA를 청록색, 이웃 평면을 초록색, Fallback을 주황색으로 표시합니다. 선은 해당 BoxCollider의 Transform, center, size와 같습니다.
- 세 종류 모두 Trigger가 아닌 BoxCollider이며, Unity Physics 충돌에 사용됩니다.
