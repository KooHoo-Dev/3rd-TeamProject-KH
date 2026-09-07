# 곡괭이 타격 피드백

## 구현과 연결

- `PickaxeItem`: DigPower 적용 전 Sprite, 색상, 셀 중심, 셀 크기를 캐싱한다. 오프라인에서는 `RequestRemoveCell`이 한 번 이상 `Damaged`/`Removed`를 반환한 뒤 VFX를 한 번 재생한다. DigPower 내부 반복 횟수만큼 재생하지 않는다.
- `TerrainManager.TryGetTileVisual`: 기존 `TryGetTileType`, `TerrainGridWorld.GetTileSprite/GetTileColor`를 사용하는 읽기 전용 조회다.
- `TerrainTileHitEffect`: 곡괭이당 독립된 SpriteRenderer 오버레이 하나를 만들고 재사용한다. 다른 셀을 타격하면 이전 효과를 종료하고 새 셀에서 재생한다. 원본 타일, 자원 표시, 하이라이트는 유지한다.
- `Assets/Plugins/Demigiant/DOTween`: 공식 무료 배포본 1.3.030. 배포본의 메타데이터를 그대로 가져왔으며 새 프로젝트 C# 파일의 `.meta`는 Unity가 생성한다.

실행 경로:

```text
좌클릭/홀드 → ItemController.TryStartMine → PickaxeItem.PrepareUse
→ MineStarted → 애니메이션 타격 이벤트
→ CharacterAnimationController → ItemController.ExecuteMineImpact
→ PickaxeItem.ExecutePreparedUse (pendingTargetCell, 타격 기회 소비/재검증)
→ ExecuteDigPower → 성공 시 PlayHitEffect → TerrainTileHitEffect.Play
```

`Use` 직접 호출 경로도 같은 `ExecuteDigPower`에 연결된다. Preview/Highlight에서는 VFX를 호출하지 않는다.

온라인에서는 서버가 실제 피해를 확정하므로, 타격 프레임에서 `RequestExcavation` 전송 성공에 대한 로컬 시각 피드백을 재생한다. 연결 준비 전, 동일 셀 요청 대기 중, 전송 실패 시에는 재생하지 않는다. 서버가 나중에 요청을 거절하더라도 이미 재생한 짧은 피드백은 취소할 수 없다. 서버 확정 성공 횟수와의 엄밀한 1:1이 아니라 **전송에 성공한 로컬 타격 요청과 1:1**이다. 서버 응답에서 다시 재생하지 않는다.

파괴 시 데이터는 즉시 변경되고 RenderChunk Mesh/경계 Collider는 기존 `LateUpdate`에서 갱신된다. 캐싱한 파괴 전 Sprite는 최대 효과 시간 동안만 보이고 이후 원위치 복구, Sprite 해제, 오브젝트 비활성화된다.

## Inspector와 Setup

필수 참조 연결은 없다. 첫 타격 시 곡괭이에 `TerrainTileHitEffect`가 자동 추가된다. 기본값으로 즉시 동작한다.

지속적으로 값을 조절하려면 Unity Editor에서 곡괭이 프리팹의 `PickaxeItem`과 같은 GameObject에 `TerrainTileHitEffect`를 직접 추가한다. Play Mode에서 자동 추가된 컴포넌트의 변경은 종료 후 저장되지 않는다.

| Inspector 값 | 기본값 | 의미 |
| --- | --- | --- |
| Shake Duration | 0.12 | 초, 일시정지에도 종료되는 비스케일 시간 |
| Shake Strength | 0.04 | 셀 크기에 대한 비율, 1유닛 셀에서 0.04유닛 |
| Shake Vibrato | 8 | 진동 수 |
| Shake Randomness | 20 | 진동 방향 무작위성 |

현재 테스트 씬 셀 크기는 1, 기본 Sprite PPU는 64다. Sprite bounds로 크기와 Pivot을 보정한다. XY만 흔들고 Z는 고정한다. 원본이 보이는 상태의 복제 연출이므로 가장자리에서 원본과 겹쳐 보일 수 있다. 최종 강도/가독성은 실제 게임 카메라에서 확인한다.

DOTween 공식 설치 안내: https://dotween.demigiant.com/download.php

Unity에서 `Tools > Demigiant > DOTween Utility Panel > Setup DOTween...`을 한 번 실행한다. 이번 효과의 Transform `DOShakePosition`은 코어 DLL 기능이며 별도 Sprite/Physics/UI 모듈이나 DOTween Pro는 필요 없다. Setup UI는 이번 작업에서 조작하지 않았다.

## 상태 격리와 정리

- Tween 대상은 `Dig Tile Hit Visual`의 Transform뿐이다. 지형/플레이어 Transform을 참조하거나 움직이지 않는다.
- 오버레이는 플레이어의 자식이 아니어서 이동, 회전, 스케일을 상속하지 않는다. Collider/Rigidbody도 생성하지 않는다.
- 기존 Terrain 변경, 내구도, 붕괴, FallingChunk, 네트워크 요청/응답, 해시 로직은 유지된다.
- 재타격 시 `DOKill → 기준 위치 복원 → 새 셀 배치 → 새 Shake` 순서다.
- 완료/외부 Kill/컴포넌트 및 곡괭이 비활성화 시 복원 및 숨김, 소유자 파괴 시 오버레이 파괴. 오버레이 자체 비활성화/파괴도 `SetLink`로 Tween을 종료한다.
- Shake 생성/초기화 전후 Unity Random 상태를 복구하여 게임용 난수 소비를 추가하지 않는다.

## 검증 및 Play Mode 체크리스트

수행: `dotnet build Assembly-CSharp.csproj --no-restore --verbosity quiet` 성공(경고/오류 0), `git diff --check`, 수정 C# 전체와 호출/정리 경로 검토. 관련 자동화 테스트는 발견되지 않았다. 아래 실제 Play Mode/서버 동작은 아직 실행하지 않았으며 통과로 간주하지 않는다.

공용 씬을 저장/수정하지 말고 별도 테스트 씬 복사본 또는 기존 씬의 Play Mode에서 확인한다.

| 조건 | 확인 방법과 기대 결과 |
| --- | --- |
| 일반 타일 1회 | 내구도가 남는 타일 타격. 입력 때는 정적 Highlight만 보이고 애니메이션 타격 때 해당 셀만 0.12초 흔들린다. |
| 연속 타격 | 테스트 중 Duration을 공격 간격보다 길게 설정하고 같은 타일 반복 타격. 재시작마다 기준 위치가 같고 종료 후 Transform이 정확히 복구된다. 확인 후 0.12로 복구. |
| 홀드 | `ExecuteDigPower`와 `PlayHitEffect`에 중단점/호출 계수 사용. 성공한 타격 하나당 한 번이며 DigPower > 1에서도 한 번이다. |
| 대상 변경 | 효과 도중 다른 셀 타격. 이전 오버레이가 남지 않고 새 셀에만 표시된다. 오버레이 오브젝트 수는 곡괭이당 하나다. |
| 파괴 | 내구도 1 타일을 타격. 파괴 전 비주얼로 짧게 재생된 후 오버레이가 비활성화되고 Sprite가 null이다. |
| 청크/플레이어/Collider | 효과 중 RenderChunk와 플레이어의 Transform, 경계 Collider를 Inspector로 관찰. VFX 때문에 좌표가 변하지 않는다. 파괴에 따른 기존 Collider 갱신은 정상이다. |
| FallingChunk | 지지 타일을 파괴해 붕괴 유발. 기존 낙하/착지 동작과 충돌이 유지되고 오버레이는 낙하체에 연결되지 않는다. |
| 온라인 | 2클라이언트에서 채굴. 요청 성공 시 로컬 1회, 서버 Batch 수신 시 중복 없음. 같은 셀의 미확정 요청/연결 실패 시 VFX 없음. 서버 거절은 로컬 피드백과 별개임을 확인한다. |
| 데이터/해시 | 동일 초기 Snapshot/동일 타격으로 VFX 컴포넌트 활성/비활성 실행 결과 비교. 내구도, Revision, 최종 지형 Snapshot/해시가 동일해야 한다. 실제 채굴 자체가 일으키는 변화는 제외한다. |
| 수명 | 효과 중 곡괭이/컴포넌트/오버레이를 각각 비활성화·파괴하고 씬 전환. `DOTween.IsTweening(오버레이 Transform)`이 다음 업데이트 이후 false이며 잔상/예외가 없어야 한다. |

남은 작업: DOTween Setup UI 확인, 위 Play Mode 체크리스트 및 멀티플레이 통합 검증, 카메라 배율에 따른 시각 강도 조절. 씬/프리팹 연결 변경은 수행하지 않았다.
