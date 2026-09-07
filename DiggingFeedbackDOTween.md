# Terrain Visual Feedback

## 변경 구조

- `PickaxeItem`: 타격 프레임의 `ExecuteDigPower`에서 오프라인 피해 성공 또는 온라인 `RequestExcavation` 전송 성공 시 `TerrainManager.PlayTileHit(cell)`만 요청한다. Crack 상태를 소유하거나 예측하지 않는다.
- `TerrainManager`: 타격 요청을 해당 `TerrainGridWorld`로 전달한다. 이전 복제용 `TryGetTileVisual`은 호출자가 없어 제거했다.
- `TerrainGridWorld`: `cellVisualOffsets`를 렌더링 상태로 소유한다. `cell → quad 시작 vertex`와 청크별 vertex 배열을 캐싱한다. 기본 셀 모서리 + Offset으로 4개 vertex를 다시 쓰며 이전 위치에 누적하지 않는다.
- `TerrainTileHitEffect`: 곡괭이의 Sprite 복제 방식에서 **지형 GameObject의 셀별 Tween 관리자**로 변경했다. SpriteRenderer/proxy Transform을 생성하지 않고 `DOTween.Shake`의 Vector3 setter를 사용한다. A/B 셀은 독립적으로 재생되고 같은 셀은 기존 Tween 종료 → Offset 0 → 새 Tween 순서다.
- `TerrainResourceViewManager`: 기존 Resource View의 기본 위치 + 동일 Offset을 사용한다. 생성 시와 기존 View 재사용 시 모두 적용한다.
- `TerrainCrackOverlayManager`: 손상 단계에 도달한 살아 있는 셀만 SpriteRenderer를 가진다. 단계 변경은 기존 Renderer의 Sprite를 교체하고 제거 시 간단한 풀에 반환한다.
- `Editor/TerrainVisualFeedbackValidation`: 임시 World를 만드는 Play Mode 회귀 검증. 런타임 코드가 Editor 타입을 참조하지 않는다.

## 균열 판정과 데이터 원천

`damage = max - current`를 계산하고 `damage * 100 >= max * threshold`를 만족하는 가장 높은 20/40/60/80 단계를 사용한다. 곱셈은 `long`으로 수행한다. 현재 내구도 ≤ 0, 최대 내구도 ≤ 0, 현재 내구도 ≥ 최대 내구도이면 표시하지 않는다.

| Max | Current 순서 | 단계 순서 |
| --- | --- | --- |
| 5 | 5, 4, 3, 2, 1, 0 | 없음, 20, 40, 60, 80, 없음 |
| 3 | 3, 2, 1, 0 | 없음, 20, 60, 없음 |

최대 내구도는 `TerrainGridWorld.TryGetMaxDurability`가 기존 맵 초기화용 `GetMaxDurability(type, resourceID)`를 그대로 호출한다. 유효한 ResourceID는 ResourceDef, 그 외는 TileDef를 사용하며 기존 fallback도 보존한다. 균열 관리자가 정의 선택을 중복 구현하지 않는다.

균열 Sprite는 최초 초기화에서 `Resources.LoadAll<Sprite>("Tile/crackOverlay20")` 등으로 네 단계만 읽어 캐싱한다. 현재 Import는 Multiple이며 각 파일에 실제 48×48 `_0` subasset 하나가 존재한다. Sprite bounds와 Pivot으로 한 셀 크기에 맞춘다. Sorting은 기존 Terrain 0 / Resource 2 위의 Crack 4이며 Highlight보다 아래다. Debug 내구도 숫자 기능은 유지한다.

## 상태 갱신

- `LoadMap`, `LoadSnapshot`: 이전 Tween/Offset을 정리한 뒤 현재 셀 상태로 `RebuildAll`을 호출한다. 손상 없는 새 맵에는 Crack View를 만들지 않는다.
- `SetCell`, `TryDamageCell`, `RemoveCell`: 성공한 셀만 `RefreshCellFeedback → RefreshCell`을 호출한다. FallingChunk가 `SetCell`로 환원될 때도 보존된 내구도가 반영된다.
- `ApplyAuthoritativeBatch`: 기존 데이터 검증과 적용이 성공한 뒤 각 변경 셀만 갱신한다. 서버 Batch 전에 로컬 Shake를 해도 Crack은 바뀌지 않는다.
- `TerrainDurabilityChanged`를 구독하지 않으므로 로컬 이벤트가 없는 서버 경로도 누락되지 않는다.
- 파괴/내구도 0: 해당 Tween을 Kill하고 Offset을 제거하며 Crack을 풀로 반환한다. Resource 제거는 기존 로직을 따른다. 파괴된 타일을 가짜 Sprite로 복원하지 않는다.

## Offset과 성능

`TrySetCellVisualOffset`은 논리 좌표, 내구도, Revision, Snapshot, 해시 입력, Collider, Rigidbody 또는 FallingChunk를 변경하지 않는다. DOTween 생성/초기화의 Unity Random 상태도 복구한다.

Shake setter는 해당 quad의 캐시 4개 값과 해당 셀의 Resource/Crack 위치만 변경한다. `LateUpdate`에서 시각 변경 청크별 vertex 배열을 한 번 업로드한다. `MarkCellDirty`를 호출하지 않으므로 Shake 때문에 지형/Collider rebuild가 발생하지 않는다. 실제 지형 변경에 따른 기존 rebuild는 유지하며, `BuildMesh`도 저장된 Offset을 읽어 Shake 도중 Batch가 와도 정렬을 유지한다.

Tween 완료/외부 Kill/컴포넌트 비활성화/파괴 때 Offset이 0으로 복원된다. World 비활성화 및 맵 교체도 모든 Offset을 복원하고 캐시를 정리한다. Crack View는 독립적인 지속 상태로 남으며, 셀이 사라지거나 맵이 바뀌면 제거·재구성된다.

## Inspector

필수 수동 연결은 없다. World가 Crack 컴포넌트를 자동 확보하고 첫 타격에 HitEffect를 자동 확보한다. 설정을 저장하려면 **TerrainGridWorld와 같은 GameObject**에 `TerrainTileHitEffect`를 추가한다.

| 설정 | 기본값 |
| --- | --- |
| Shake Duration | 0.12초 |
| Shake Strength | 셀 크기의 0.04 |
| Shake Vibrato | 8 |
| Shake Randomness | 20 |

이전 버전에서 곡괭이에 HitEffect를 직접 추가했다면 해당 컴포넌트는 더 이상 사용되지 않는다. Editor에서 지형 쪽으로 설정을 옮긴다. 씬/프리팹은 이번 코드 변경에서 편집하지 않았다. 설치된 DOTween을 그대로 사용하며 추가 패키지는 없다.

## 검증 실행 방법

Play Mode에서 `Tools > Terrain Collapse > Validate Visual Feedback (Play Mode)`를 실행한다. 검증은 임시 GameObject/World만 만들고 마지막에 정리한다. 결과는 Console 및 `Logs/TerrainVisualFeedbackValidation.txt`에 기록한다. 프로젝트 전역 DOTween 초기화와 데이터 테이블 로드를 사용할 수 있으므로 실제 세션 중보다 별도 테스트 씬에서 실행하는 것을 권장한다.

검사 범위: Max 5/3 단계, 정수 overflow, Snapshot 초기 복원, Resource 최대 내구도, 단일 quad 이동, Resource/Crack 동시 이동, 위치 비누적, Offset 전후 Snapshot·Revision 동일성, Collider 객체/점 유지, Batch 뒤 Mesh rebuild 정렬, 독립/반복 Tween, 완료·파괴·비활성화 정리, 로컬 피해, View 풀 재사용, 맵 재로딩.

별도 수동 확인: 실제 카메라에서 균열 가독성/진동 강도, 굴착 애니메이션 및 홀드 타격 수, 실제 두 클라이언트/서버 통신과 중간 참여, 광물 Drop·붕괴·낙하체 착지의 전체 게임 동작. 테스트의 Batch/Snapshot 직접 적용은 실제 네트워크 통합 테스트를 대신하지 않는다.

원격 플레이어의 타격을 새로 수신하는 네트워크 이벤트는 추가하지 않았다. 동일 World에 들어온 서로 다른 셀의 타격 요청을 독립 처리할 수 있으며, 현재 호출 경로는 기존 로컬 곡괭이 경로다. Grid Transform의 임의 회전/스케일은 기존 지형 좌표계의 지원 범위를 확장하지 않는다.

## 이번 작업의 실행 결과

- Unity 6000.3.15f1의 별도 임시 프로젝트에서 Play Mode 회귀 검사 전체 통과. `-batchmode -nographics` 실행이므로 실제 화면의 시각적 품질을 확인한 것은 아니다.
- 검사한 7개 C# 파일과 현재 작업 파일의 SHA-256 일치 확인.
- 광물 프리팹의 실제 생성, Mesh/Resource/Crack 동일 Offset, 신규 균열의 Offset 상속, Batch rebuild, 독립/반복/완료 Tween, 파괴/비활성화/컴포넌트 파괴, Snapshot/Map 재로딩, Collider 및 Snapshot 불변성 통과.
- 런타임/Editor C# 빌드 성공(경고 0, 오류 0). `git diff --check` 통과. 추가한 C#에 단항 논리 부정 연산자 없음.
- 검증 결과: `Logs/TerrainVisualFeedbackValidation.txt`, Unity 실행 로그: `Logs/TerrainVisualFeedbackValidation-Unity.log` (로컬 검증 산출물).
- 임시 프로젝트를 Windows 8.3 단축 경로로 열었을 때 MonoScript 타입 연결이 누락되는 문제가 있었다. 실제 긴 경로로 실행한 최종 검사는 원본 프리팹/스크립트 그대로 통과했다. Batch 검사를 재현할 때도 실제 긴 경로를 사용한다.
- 미실행: 실제 카메라 화면/조작감, 두 클라이언트와 서버를 연결한 통합 플레이, 전체 Drop/Collapse/FallingChunk 게임 흐름. 위 수동 확인 항목으로 남긴다.
