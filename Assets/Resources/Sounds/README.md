# 사운드 파일 배치

이 폴더에 다음 이름으로 오디오 파일을 넣고 Unity의 Import 완료 후 Play Mode를 시작하세요.
Inspector에서 연결하거나 씬/프리팹을 수정할 필요는 없습니다.

| 파일 | 재생 시점 |
| --- | --- |
| dig.wav | 유효한 채굴 타격. 온라인에서는 채굴 요청을 보낸 시점의 로컬 피드백 |
| collapse.wav | 낙하 지형 청크 생성. 연속 0.06초 내 중복 재생 억제 |
| player_hurt.wav | 로컬 플레이어의 실제 HP 감소량이 한 번에 3 이상. 온라인은 서버 확정값 사용 |
| player_death.wav | 로컬 플레이어 사망 확정 |
| player_land.wav | 일반 이동에서 0.1초 이상 공중에 있다가 착지 |
| player_run.wav | 지상에서 실제 수평 이동 중 0.3초 간격. 짧은 발소리 한 번짜리 파일 권장 |
| explosion.wav | 다이너마이트 또는 지뢰의 폭발 확정 |
| dynamite_tick.wav | 다이너마이트 투척 직후부터 폭발 확정 전까지 반복되는 시계음 |
| potion_drink.wav | 포션 사용이 확정되어 체력이 회복될 때 |

### 효과음 변형

아래 폴더에 오디오 파일을 여러 개 넣으면 재생할 때마다 무작위로 하나를
선택합니다. 파일명은 자유이며 `.wav`, `.ogg`, `.mp3`를 사용할 수 있습니다.

| 폴더 | 용도 | 예시 |
| --- | --- | --- |
| `Assets/Resources/Sounds/DigVariants/` | 채굴 타격음 | `dig_01.wav`, `dig_02.ogg` |
| `Assets/Resources/Sounds/HurtVariants/` | 플레이어 피격음 | `hurt_01.wav`, `hurt_02.ogg` |
| `Assets/Resources/Sounds/DeathVariants/` | 플레이어 사망음 | `death_01.wav`, `death_02.ogg` |
| `Assets/Resources/Sounds/CollapseVariants/` | 지형 붕괴음 | `collapse_01.wav`, `collapse_02.mp3` |
| `Assets/Resources/Sounds/LandVariants/` | 플레이어 착지음 | `land_01.wav`, `land_02.ogg` |
| `Assets/Resources/Sounds/RunVariants/` | 플레이어 달리기 발소리 | `step_01.wav`, `step_02.ogg` |
| `Assets/Resources/Sounds/ExplosionVariants/` | 다이너마이트·지뢰 공용 폭발음 | `explosion_01.wav`, `explosion_02.ogg` |
| `Assets/Resources/Sounds/PotionDrinkVariants/` | 포션 사용음 | `potion_01.wav`, `potion_02.ogg` |

클립이 둘 이상이면 직전에 재생한 클립은 다음 선택에서 제외합니다. 변형 폴더에 오디오가 하나라도
있으면 해당 효과음의 기존 단일 파일 대신 폴더 안의 파일만 사용합니다.
변형 폴더가 비어 있으면 기존 단일 파일을 그대로 재생합니다.

## 배경음악

씬에 따라 아래 파일을 자동으로 불러와 반복 재생합니다.

| 파일 | 재생 씬 |
| --- | --- |
| `menu_bgm.wav` | `MainMenuScene`, `RoomLobbyScene` |
| `ingame_bgm.wav` | `InGame` |
| `bgm.wav` | 씬 전용 음악이 없거나 다른 씬을 실행할 때 사용하는 기본 음악 |

메인 화면에서 로비 화면으로 이동할 때는 같은 `menu_bgm`을 계속 재생하므로 음악이 다시 시작되지
않습니다. 인게임 진입 시 `ingame_bgm`으로 교체되고, 메인 또는 로비로 돌아오면 다시 `menu_bgm`으로
교체됩니다. 씬 전용 파일이 없으면 기존 `bgm`을 대신 재생합니다.
인게임에서는 전용 또는 대체 배경음악을 `0.5`, 메인·로비와 기타 씬에서는 `1.0` 볼륨으로
자동 설정합니다.

씬이 바뀌어도 음악 재생 오브젝트는 유지되며, 별도로 Inspector나 프리팹에 연결할 필요가 없습니다.

다른 이름의 음악을 재생하거나 실행 중 곡을 바꾸려면 확장자를 제외한 파일명을 전달하세요.

```csharp
GameAudio.GameSound.PlayMusic("mine_bgm"); // Assets/Resources/Sounds/mine_bgm.* 재생
GameAudio.GameSound.SetMusicVolume(0.5f);  // 0~1 범위
GameAudio.GameSound.StopMusic();
```

배경음악도 `.wav`, `.ogg`, `.mp3`를 지원하며 같은 이름의 파일은 하나만 배치하세요.
씬 전용 파일과 기본 `bgm` 파일이 모두 없으면 Warning이 발생하고 배경음악만 재생하지 않습니다.

확장자는 .ogg 또는 .mp3도 가능합니다. 같은 이름의 파일은 하나만 배치하세요.
파일이 없으면 실행 시작 시 파일별 Debug.LogError가 한 번 발생하며 해당 소리만 건너뜁니다.
Console의 Error Pause가 켜져 있으면 이 의도된 오류로 실행이 멈출 수 있습니다.
Play Mode 도중 파일을 추가했다면 Play Mode를 다시 시작하세요.

효과음은 타격 지점, 붕괴 지점, 각 플레이어 위치에 생성되는 3D AudioSource에서 재생됩니다.
2유닛까지는 원본 음량이며 18유닛에서 들리지 않도록 선형 감쇠합니다. 2D 게임의 XY 거리만
반영되도록 효과음 원점의 Z 좌표는 활성 AudioListener와 맞춥니다. 배경음악은 감쇠 없는 2D입니다.
로컬과 원격 플레이어 모두 이동, 착지, 피해, 사망 효과음이 각 플레이어 위치에서 재생됩니다.
포션 사용음도 실제 회복이 확정된 플레이어 위치에서 재생되어 같은 거리 감쇠를 적용받습니다.
로컬 채굴음은 실제 타격 타일 중심에서, 원격 채굴음은 플레이어의 곡괭이 방향 타격 지점에서 재생됩니다.
게임 씬에는 활성 AudioListener가 필요합니다.
달리기를 멈추면 추가 발소리가 발생하지 않으며 이미 시작한 짧은 소리는 끝까지 재생됩니다.
재생용 임시 오브젝트는 클립 길이 이후 또는 씬 종료 시 제거됩니다.
다이너마이트 시계음은 다이너마이트를 따라 이동하며 폭발할 때 즉시 정지됩니다. 자연스러운 반복을
위해 클립 시작과 끝이 매끄럽게 이어지는 짧은 루프 파일을 권장합니다.
같은 `dynamite_tick` 클립을 게임 종료 10초 전부터 거리 감쇠 없는 2D 경고음으로도 끊김 없이
반복 재생합니다. 종료 경고음은 `1.5` 볼륨 배율이며 게임 종료 또는 연결 해제 시 즉시 정지됩니다.

## Unity 확인 항목

- 여섯 효과음 파일 Import 후 누락 오류가 사라지는지 확인합니다.
- 메인과 로비에서 menu_bgm이 이어지고, InGame 진입 시 ingame_bgm으로 바뀌는지 확인합니다.
- 채굴 타격과 붕괴 때 소리가 나오는지 확인합니다.
- 온라인과 오프라인에서 다이너마이트·지뢰 폭발음이 폭발당 한 번 재생되는지 확인합니다.
- 다이너마이트 투척부터 폭발까지 시계음이 위치를 따라가며 반복되고 폭발 시 정지하는지 확인합니다.
- 게임 종료 10초 전부터 1.5 배율의 2D 시계음이 반복되고 종료 시 즉시 정지하는지 확인합니다.
- 1~2 피해에는 피해음이 없고 3 이상 실제 HP 감소에는 피해음이 있는지 확인합니다.
- 온라인/오프라인 사망, 중복 서버 사망 메시지, 부활 후 재사망을 확인합니다.
- 초기 서버 체력 동기화와 회복 시 피해음이 없는지 확인합니다.
- 점프 착지, 지상 이동, 정지, 벽에 막힘, 등반, 매몰, 사망 상태에서 발소리를 확인합니다.
- 볼륨, 발소리 길이, 여러 효과음의 겹침을 실제 클립으로 확인합니다.
