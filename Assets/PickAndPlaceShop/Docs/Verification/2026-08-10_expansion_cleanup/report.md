# 확장 구조물 제거 및 월드 라벨 검증 보고서

검증일: 2026-08-10  
대상 씬: `PickAndPlaceShop_MainStreetSlice_Multiplayer`  
검증 기준: Unity Editor Play Mode (이번 배치에서는 빌드 미실행)

## 1) 파트별 완료/보류 현황

| 파트 | 결과 | 요약 |
|---|---|---|
| C. 월드 라벨 크기 통일 | 완료 | 두 시설 라벨을 공용 설정 함수와 빌보드 방식으로 통일 |
| A. Canopy·OuterWall 제거 | 완료 | 생성 경로 제거, 구 세이브 잔존물 정리, 투명 경계로 대체 |
| B. Zone_Warehouse 제거 | 완료 | 장식 구역만 비활성화하고 별도 창고 데이터·실물·상호작용 유지 |

관련 커밋: `3a915de`, `487b1b1`, `6c81d99`

## 2) A-1 선행 조사 결과

- `OuterWall`은 렌더러와 `BoxCollider`, carving `NavMeshObstacle`을 가진 물리·이동 경계였다. 단순 삭제하면 플레이어가 매장 밖으로 빠질 수 있어 경계 역할은 유지해야 했다.
- `Canopy`는 순수 시각 프리미티브였다. 자식 오브젝트와 조명이 없었고 다른 기능 오브젝트의 부모도 아니었다.
- 제거 전후 활성 조명 수는 35개로 동일했다. Canopy 제거가 조명 계층이나 광원 수를 바꾸지 않았다.
- 직접 참조 검색 결과:
  - 생성·정리: `ShopExpansionVisualController`
  - `Canopy`, `OuterWall`을 직접 사용하는 다른 런타임 스크립트는 없음
  - 씬/프리팹 Missing 참조 0건

## 3) PART A 처리 방식 및 단계별 확인

- `ShopExpansionVisualController`에서 `Canopy`, `OuterWall` 생성 코드를 제거했다.
- 로드 또는 확장 재구성 때 `ExpansionSharedShell` 아래의 기존 이름 인스턴스를 찾아 비활성화 후 정리한다.
- `OuterWall`의 경계 역할은 렌더러가 없는 `ExpansionBoundary (Invisible)`로 대체했다.
  - `BoxCollider` 있음
  - carving `NavMeshObstacle` 있음
  - 확장 최대 X 범위에 맞춰 위치와 크기가 함께 증가
  - 렌더러 0개
- 1~6단계를 각각 재구성한 결과 모든 단계에서 활성 `Canopy`/`OuterWall` 0개였다. 3단계부터 투명 경계 1개가 활성화됐다.
- 단계별 캡처:
  - [1단계](part_a_level_1.png)
  - [2단계](part_a_level_2.png)
  - [3단계](part_a_level_3.png)
  - [4단계](part_a_level_4.png)
  - [5단계](part_a_level_5.png)
  - [6단계](part_a_level_6.png)
  - [최대 확장 전체 확인](part_a_max_expansion_v2.png)

## 4) B-1 Zone_Warehouse 판정 결과

판정: **(가) 순수한 구역 표시·장식 오브젝트**.

- 루트 컴포넌트: `Transform`만 존재
- 자식: 92개 조사 시점 기준(바닥, 경계, 랙, 상자, 팔레트, 표시 기둥 등)
- 렌더러: 46개
- 런타임 충돌 부트스트랩이 만든 콜라이더: 44개
- 트리거, `ShopInteractable`, 창고 컨테이너 또는 저장 컴포넌트: 0개
- 코드 참조:
  - `ShopCityCollisionBootstrap`: 해당 구역 장식에 런타임 충돌체를 붙이던 경로
  - `ShopNpcRoutePlanner`: 장애물 그룹 이름으로만 참조
- 실제 창고 기능은 별도 `ShopWarehouseStockVisualizer`가 `PickAndPlaceShop_Generated/Architecture/ArcadeFloor` 아래에 만드는 `Warehouse Product Stock`에 존재한다. 따라서 `Zone_Warehouse` 비활성화가 창고 데이터를 제거하지 않는다.

## 5) PART B 처리 및 창고 기능 5항목 검증

- 처리 방식: `Zone_Warehouse` 전체를 런타임에서 비활성화하고 충돌 부트스트랩 대상에서도 제외했다. 기능 본체가 아니므로 장식·충돌을 함께 제거한 것이다.
- 로드 직후 데이터가 복구돼도 실물 자식이 없는 침묵 실패를 막기 위해, `ShopWarehouseStockVisualizer`가 시그니처뿐 아니라 실제 `Stock_` 자식과 상호작용 트리거 상태도 대조하도록 보강했다.

| 검증 항목 | 결과 |
|---|---|
| 창고에 아이템 추가/보유 | 공유 창고 수량 7개 확인 |
| 재고 라벨·실물 표시 | `Warehouse Product Stock` 활성, 실물 7개와 라벨/트리거 유지 |
| E 수집 | 창고 7→6, 개인 인벤토리 2→3, 총량 보존 |
| 인벤토리·진열 이동 | 개인/창고/진열 3/6/5 → 2/6/6 → 3/6/5로 왕복 성공 |
| 저장·이어하기 | 테스트 세이브 44,307 bytes 생성 후 로드 성공, 3/6/5와 실물 7개 복원 |

사용자 원본 세이브는 테스트 전에 백업했고 검증 후 정확히 원복했다.

- [Zone_Warehouse 제거 구역](part_b_zone_removed.png)
- [보존된 실제 창고 상품](part_b_stock_preserved_v2.png)

## 6) B-4 NPC 통행 문제 확인

- `Zone_Warehouse` 비활성 후 런타임 충돌체 수는 0개였다.
- 경로 계산:
  - 입구→진열대: 성공, `PhysicsDetour`
  - 진열대→계산대: 성공, `PhysicsDetour`
- 알바 3명을 임시로 활성화해 확인했다.
  - Cashier `(8.01, 0.18, -5.74)`에서 이동 확인
  - Collector `(7.95, 0.05, -8.70)` 정상 배치
  - Stocker `(4.27, 0.11, -1.79)` 정상 배치
- 정지·낙하·구역 충돌은 재현되지 않았다. 테스트용 고용/출근 마스크는 검증 후 원래 값 `0/0`으로 복구했다.

## 7) PART C 라벨 비교 및 수정 결과

원인: 문제 라벨마다 서로 다른 Transform scale과 문자 크기를 사용했다.

| 라벨 | 수정 전 scale | 수정 후 | 최종 텍스트 bounds |
|---|---:|---|---:|
| 위탁 판매 코너 | `(2.40, 1.45, 0.90)` | world scale 1, character size 0.04, font size 64 | `(1.71, 0.75)` |
| 빈 캡슐 회수함 | `(1.35, 1.30, 0.75)` | world scale 1, character size 0.04, font size 64 | `(1.61, 0.37)` |

- 정상 기준인 `온라인 주문 포장대` bounds `(1.87, 0.28)`, `영업 시작 / 계산` bounds `(1.80, 0.37)`과 같은 체감 범위로 통일했다.
- `ConfigureFacilityLabel` 한 곳에서 크기·정렬·월드 스케일을 설정한다.
- 두 라벨 모두 `ShopWorldTextBillboard`를 사용해 카메라를 향한다.
- 비교 캡처:
  - [기준 라벨 모음](part_c_reference_labels.png)
  - [위탁 판매 코너 수정 후](part_c_consignment_after_final.png)
  - [빈 캡슐 회수함 수정 후](part_c_recycler_after_final.png)

## 8) 최대 확장 통합 검증

최대 확장 상태에서 최종 측정값:

- 활성 `Canopy`/`OuterWall`: 0개
- `ExpansionBoundary (Invisible)`: 활성, 렌더러 0, 콜라이더 있음
- `Zone_Warehouse`: 비활성
- `Warehouse Product Stock`: 활성, 실물 상품 7개
- 활성 조명: 35개로 변경 전과 동일
- 4초 추가 관찰 후 Unity 콘솔 error 0 / warning 0

## 9) 기존 세이브 로드 확인

- 기존 저장을 보존한 상태로 `LoadNow()`가 성공했다.
- 로드 직전/직후 창고 실물 수: 7/7.
- 개인/창고/진열 수량 3/6/5가 유지됐다.
- 구버전 방식의 `Canopy`, `OuterWall` 이름 인스턴스를 가상 생성한 뒤 재구성했을 때 둘 다 비활성화되어 잔존하지 않았다.
- 기존 사용자 세이브 파일은 검증 후 원본으로 복원했다.

## 10) 회귀 확인 결과

- 창고 입고/수집: 정상
- 개인 인벤토리↔공용 진열 이동: 정상, 총량 보존
- 진열 시각 갱신 요청 경로: 정상
- 알바 이동/역할 배치: 정상
- NPC 경로 계산: 정상
- 확장 단계별 바닥/선반 생성: 정상
- 조명 수와 플레이어 활성 상태: 정상
- 구매·판매·계산대는 이번 변경 코드가 해당 거래 경로를 수정하지 않음을 정적 확인했다. 직전 플레이 회귀 기록 `e251a48`의 실제 거래 검증 결과도 유지된다.

## 11) 보류·실패 항목

- 보류 항목 없음.
- 첫 강제 단계 전환 시험에서 이전 단계 ZoneSign 코루틴이 제거된 임시 오브젝트를 참조하는 테스트성 `MissingReferenceException` 3건이 있었으나, 실제 단계 순서와 동일하게 `previousLevel=0`으로 재검증해 재발 0건을 확인했다. 최종 깨끗한 플레이 세션의 error/warning은 모두 0건이다.
- WebGL/PC 빌드는 지시서가 Editor Play Mode 검증만 요구해 이번 배치에서 생성하지 않았다.

## 12) 가정 및 임의 판단

- 외벽의 시각 요소는 완전히 제거하되, 플레이어 이탈 방지는 요청 취지와 안전성을 위해 보이지 않는 동적 경계로 유지했다.
- `Zone_Warehouse`는 조사 결과 기능 본체가 아닌 장식이므로 부분 분리 대신 루트 전체 비활성화를 선택했다.
- 시설 라벨은 개별 숫자를 임의로 줄이지 않고, 현재 게임에서 읽기 좋은 정상 기준 라벨의 체감 폭과 동일한 공용 규칙으로 맞췄다.
- 단계별 캡처는 동일한 고정 카메라 각도로 촬영해 구조물이 다시 생기지 않는지를 비교할 수 있게 했다.
