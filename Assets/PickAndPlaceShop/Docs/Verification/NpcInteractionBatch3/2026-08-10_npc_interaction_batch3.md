# NPC 행동·상호작용 3차 배치 검증 보고서

- 검증일: 2026-08-10
- 대상: ToyGame / `PickAndPlaceShop_MainStreetSlice_Multiplayer`
- 검증 환경: Unity Editor 6000.4.6f1, Host Play Mode
- 최종 판정: **PART A, C, B 모두 완료**
- 빌드: 이번 지시서 범위에 따라 만들지 않음
- 선행 저장 수정 확인: `5557df3 fix(save): restore continue session state`

## 1. 완료 현황

| 파트 | 결과 | 핵심 결과 | 커밋 |
|---|---|---|---|
| A. CityScenery 쓰레기통 | 완료 | 대상 2개 모두 E·공격 상호작용, 일일 합산 500원, 저장 복원 확인 | `7f7e705` |
| C. 알바 경로 | 완료 | 계산대·진열대·창고·기계 충돌 회피, 3분 관찰 중 관통·끼임 0건 | `b3454c2` |
| B. 경찰 안전 구역 | 완료 | 매장 안 추격 중단·복귀, 밖에서는 체포, 재출동 쿨다운 적용 | `2355fd6` |
| 회귀 수정 | 완료 | Play Mode 종료 시 입력 관리자 재생성 오류 제거 | `f88025d` |

## 2. PART A — CityScenery 쓰레기통

### 전체 조사와 원인

`CityScenery/CITY_Props` 아래에서 쓰레기통 계열 원본을 전수 조사했다. 해당 씬의 실제 대상은 다음 2개다.

| 원본 오브젝트 | 수정 전 Collider | 수정 전 상호작용 | 수정 후 런타임 루트 | 결과 |
|---|---:|---:|---|---|
| `S2_p` | 0 | 0 | `TrashInteractionRoot_1` | 정상 |
| `S2_p.001` | 0 | 0 | `TrashInteractionRoot_2` | 정상 |

기존 구현은 런타임 등록 대상을 `S2_p.001` 하나로 고정하여 다른 동일 계열 오브젝트를 놓쳤다. 두 원본 모두 자체 Collider와 상호작용 컴포넌트가 없어서 프롬프트와 E 입력이 감지될 수 없었다.

### 수정 내용

- `ShopSideContentRuntime.ConfigureTrash`가 CITY_Props 하위의 실제 쓰레기통을 이름 기준으로 전부 수집한다.
- 각 대상에 공용 구성인 `BoxCollider`, carving `NavMeshObstacle`, `ShopInteractionTrigger`, `ShopInteractable(TrashSearch)`, 안내 라벨을 같은 방식으로 붙인다.
- 전용 획득 경로를 만들지 않고 기존 쓰레기 탐색·중앙 자금 증가 함수를 그대로 사용한다.
- 일일 누적 한도는 쓰레기통별이 아니라 전체 합산 500원으로 유지한다.

### Play Mode 검증

- 생성 위치
  - `TrashInteractionRoot_1`: `(64.00, 1.15, -6.14)`
  - `TrashInteractionRoot_2`: `(17.73, 0.45, -5.98)`
- 두 루트 모두 solid collider / obstacle / trigger / action / label 존재를 확인했다.
- E 경로: `ShopInteractable.Interact()`를 실제 플레이어 E 입력과 동일한 호출 경로로 실행하여 누적 `499 → 500`, 자금 `+1`을 확인했다.
- 공격 경로: 두 쓰레기통 각각 공격 성공, 누적 한도와 자금 증가를 확인했다.
- 합산 한도: 누적 499원에서 여러 쓰레기통을 연속 시도했을 때 첫 성공만 500원까지 반영되고 이후 자금 증가는 0원이었다.
- 날짜 내 저장 복원: 321원이 321원으로 복원됐다.
- 다음 날 복원: 이전 날짜 444원은 0원으로 초기화됐다.
- 테스트 중 변경한 확률·자금·누적 상태는 모두 원래 값으로 복구했다.

## 3. PART C — 알바·손님 경로

### 실제 관찰과 원인

- 계산대 bounds: 중심 `(7.80, 0.65, -4.50)`, extents `(1.70, 0.65, 0.90)`
- 수정 전 계산 알바 목표 `(8.20, 0.05, -3.50)`가 계산대 내부/반대편으로 잡혀 경로가 계산대를 통과했다.
- 밀집된 최대 확장 씬에서 알바의 overlap buffer 24개가 먼저 채워져 계산대 충돌체를 누락할 수 있었다.
- 우회 경로는 첫 번째 구간만 검증하고 두 번째 구간이 막혀도 직선 이동으로 되돌아가 구조물을 관통할 수 있었다.

### 수정 내용

- 알바 충돌 검사 버퍼를 24개에서 96개로 확장했다.
- 목적지가 구조물 내부이면 8방향·여러 반경에서 안전한 작업 위치를 찾는다.
- 우회 경로의 두 구간을 모두 검증하고, 두 번째 구간이 막혔을 때 직선 관통 대신 안전한 중간 지점까지만 이동한다.
- 계산대, 창고 구역, 공용 진열대, 쓰레기통 상호작용 루트를 동일한 장애물 그룹으로 판정한다.

### 3분 Play Mode 관찰

45초 단위 4회, 총 3분 동안 역할과 배치를 바꾸어 관찰했다.

| 구간 | 배치 | 구조물 관통 | 계산대 끼임/제자리 걸음 | 도착 결과 |
|---|---|---:|---:|---|
| 1 | 기본 역할 | 0 | 0 | 정상 |
| 2 | 2·3번 알바를 서로 다른 뽑기/쿠지 기계에 배치 | 0 | 0 | 재고 알바 `(13.1, 0.1, 1.5)`, 수거 알바 `(58.7, 0, 4.3)` 도착 |
| 3 | 기본 역할 복원 | 0 | 0 | 정상 |
| 4 | 최대 확장 최종 관찰 | 0 | 0 | 정상 |

최대 확장 계산 알바는 `(7.14, 0.11, -7.36)`에서 안전 우회점 `(6.51, 0.05, -6.50)`을 사용해 목표 `(8.24, 0.05, -3.20)`으로 이동했고, 계산대 bounds 교차와 overlap은 모두 false였다.

손님도 같은 확장 상태에서 `Enter`, `Browse`, `InspectProduct`, `Queue` 상태로 진열대와 계산대에 접근했으며 구조물 overlap 0건을 확인했다.

증거: [최대 확장 알바](/Assets/PickAndPlaceShop/Docs/Verification/NpcInteractionBatch3/part_c_staff_max_expansion.png), [최대 확장 손님](/Assets/PickAndPlaceShop/Docs/Verification/NpcInteractionBatch3/integrated_max_expansion_customers.png)

## 4. PART B — 경찰 안전 구역

### 영역 판정과 원인

확장 전체를 포함하는 재사용 가능한 안전 Zone/Trigger는 없었다. 기존 `IsInsideShop`은 계산대 기준 반경 18m만 사용하여 확장 단계와 실제 매장 바닥 형태를 반영하지 못했다.

새 Trigger를 별도로 중복 생성하지 않고 `ShopExpansionVisualController`가 관리하는 기본 매장 바닥과 활성 확장 바닥 Renderer bounds를 권위 있는 매장 영역으로 재사용했다. 기본 `ShopFloor`, `ShopFloor (1)`과 현재 활성화된 확장 `Floor`를 수집하며 XZ 0.2m, Y 4m 여유를 적용한다.

### 동작

- 매장 밖: 기존과 동일하게 추격·체포한다.
- 추격 중 매장 진입: 즉시 복귀 상태로 전환한다.
- 복귀: 현재 확장 바닥 전체의 바깥쪽에서 가장 가까운 출구 후보로 자연스럽게 이동한 뒤 사라진다.
- 제자리 정지 상태는 남기지 않는다.
- 복귀/체포 후 재출동 쿨다운: 8초.
- 복귀 소멸 거리: 0.35m.

### B-6 검증

| 항목 | 결과 |
|---|---|
| 매장 밖에서 체포 | 체포 횟수 `+1`, 경찰 비활성화 확인 |
| 매장 안에서 체포하지 않고 복귀 | `returning=true`, 체포 증가 0, 이후 정상 비활성화 |
| 추격 중 매장 진입 시 추격 포기 | 진입 프레임에서 복귀 상태로 전환 |
| 경찰 제자리 잔존 없음 | 복귀 위치 도착 후 소멸 확인 |
| 최대 확장 전체 안전 | 활성 바닥 6개 각각의 중심에서 추격 시작 후 안전 중단, 체포 증가 0 |

즉시 재출동은 false였고 남은 쿨다운은 8.00초였다. 경찰 회전도 유지됐다. 추격 시 경찰 forward와 플레이어 방향의 내적이 약 0.71로 양수여서 플레이어/이동 방향을 바라보며 이동했다.

증거: [경찰 추격·회전](/Assets/PickAndPlaceShop/Docs/Verification/NpcInteractionBatch3/part_b_police_chase_rotation.png)

## 5. 최대 확장 통합 검증

- 확장 레벨 6, 활성 매장 바닥 6개.
- 손님 5명 동시 활동: Enter/Browse/InspectProduct/Queue 상태가 정상 진행됐다.
- 구조물·계산대 overlap 0건.
- 실제 단일 획득 함수로 상품을 확보하고, 컨테이너 이동 API로 진열 재고 8개를 배치했다.
- 손님이 `턱시도냥 잠자는 인형`을 예약했고 알바 계산을 시작했다.
- 계산 완료 결과: 판매 `0 → 1`, 오늘 판매 1, 매출 184원, 진열 재고 정상 차감.
- 알바 이동, 손님 구매·계산, 경찰 출동/복귀, 쓰레기통 E·공격을 같은 최종 코드 상태에서 확인했다.
- Play Mode 중 오류·경고 0건.
- Play Mode 종료 후 오류·경고 0건.

## 6. 추가 회귀 수정

Play Mode 종료 과정에서 `ShopStoreNamingSystem.OnDestroy`가 HUD 억제를 해제할 때 이미 파괴된 `ShopInputModeManager`를 다시 생성하여 `[Input] Mode Manager`가 남는 종료 오류를 발견했다. 억제 해제 요청은 인스턴스가 없으면 바로 반환하도록 최소 수정했고, Play 시작/종료를 다시 반복해 미정리 오브젝트와 콘솔 오류가 모두 0건임을 확인했다.

## 7. 가정과 범위

- CITY_Props의 쓰레기통 모델은 현재 씬에서 확인된 `S2_p`, `S2_p.001` 두 개를 기준으로 했다. 같은 이름 계열이 추가되면 런타임 전수 수집에 포함된다.
- 매장 안전 구역은 시각·확장 시스템이 실제 활성화한 바닥 Renderer bounds를 기준으로 한다.
- 경찰은 가장 가까운 활성 매장 바닥 외곽으로 복귀한 뒤 소멸하며, 8초 후에만 재출동할 수 있다.
- 이번 배치는 사용자 지시대로 Unity Editor Play Mode 검증까지만 수행했으며 WebGL/플레이어 빌드는 생성하지 않았다.
- 사용자 작업 중이던 메인 씬 수정과 `_Recovery` 파일은 수정·스테이징하지 않았다.

## 8. 커밋

- `7f7e705 fix(side-content): enable every city trash can`
- `b3454c2 fix(navigation): route staff around counters`
- `2355fd6 fix(theft): make shop a police safe zone`
- `f88025d fix(input): avoid teardown manager recreation`
