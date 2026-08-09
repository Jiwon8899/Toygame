# NHN 공모전 GitHub Pages 배포 검증 보고서

- 검증일: 2026-08-09
- Unity: 6000.4.6f1
- 대상 저장소: https://github.com/Jiwon8899/Toygame
- 예정 Pages URL: https://jiwon8899.github.io/Toygame/

## WebGL 빌드

- 출력 경로: `C:/Users/tommy/Desktop/ToyGame/docs`
- 빌드 결과: 성공, 오류 0, 경고 73, 923.02초
- Unity 빌드 보고 크기: 862.47MB
- 디스크 산출물 합계: 792.25MB
- 압축: Gzip, Decompression Fallback 활성화
- `.nojekyll`: 생성 완료
- 기존 `Build/WebGL`은 보존함

## GitHub 파일 제한 대응

- 원본 `docs.data.unityweb`: 812,847,054바이트
- 원본 SHA-256: `023fdbf58c1bb4a228870060f77e423a5ba20b3fe775ffb67b7287568dcebf1c`
- 90MiB 단위 9개 청크로 분할함
- 가장 큰 청크: 94,371,840바이트(90MiB)
- 분할 청크를 순서대로 다시 해시한 결과 원본 SHA-256과 일치함
- 커밋 후보 중 100,000,000바이트 이상 파일: 0개
- 재빌드 후에도 자동 분할되도록 Editor postprocessor와 WebGL 템플릿을 추가함

## 브라우저 실제 플레이 검증

- 로컬 주소: `http://127.0.0.1:18080/?split=1`
- 분할 청크 로드 후 타이틀 화면 표시: Passed
- 게임 시작 → 혼자 시작 → 이어하기: Passed
- 메인 게임플레이 씬 진입 및 HUD 표시: Passed
- 치명적 JavaScript/메모리 오류: 0건
- 비치명적 경고: WebGL/WebKit에서 지원하지 않는 일부 URP/VFX 숨김 셰이더와 지연 오디오 메타데이터 경고가 출력되지만 게임 진행은 정상임
- 증거 화면: `2026-08-09_contest_split_webgl_gameplay.png`

## Unity 검증

- 새 Editor 스크립트 컴파일: 오류 0
- 최종 Asset refresh 후 Console error: 0
- Assets `.meta` 누락: 0

## Git 및 민감정보 검사

- 기존 커밋 수: 117
- 기존 remote: 없음
- 커밋 후보 파일: 4,950개, 총 4,279.16MB
- `docs`: 16개 파일, 792.25MB
- 고위험 토큰·개인키 패턴: 0건
- `.env`, `.pem`, `.pfx`, `.p12`, `.key`, `.keystore`, `.jks`: 0건
- 저장소 Git object pack: 1.66GiB
- Unity가 생성한 YAML의 빈 문자열 필드에서 기존 trailing whitespace가 일부 확인되며 컴파일/실행에는 영향 없음

## 판정

- WebGL 빌드 및 분할 로더: Passed
- 로컬 브라우저 실제 플레이: Passed
- GitHub 파일당 100MB 제한: Passed
- Unity Console error 0: Passed
- 민감정보 검사: Passed
- 원격 저장소 설정·push·Pages 활성화: 원격 작업 결과를 별도로 기록할 것
