# Chamele-ON Prototype

Unity URP로 제작 중인 **Chamele-ON** iOS 프로토타입입니다. 스마트폰에서 빠르게 테스트할 수 있도록 블록형(복셀풍) 맵과 단순한 큐브 캐릭터로 구성했으며, 3D 바디 페인팅·화면 색상 추출·포즈 변경·벽 부착을 테스트할 수 있습니다.

## 필요한 버전

- Unity `6000.3.19f1` 권장 (Unity 6.3 LTS, Apple Silicon). 프로젝트는 `6000.3.2f1`에서도 열리지만 해당 버전에는 패키지 서명 오판 버그가 있습니다.
- iOS 빌드 시 Unity Hub의 **iOS Build Support** 모듈
- iOS 아카이브/TestFlight 업로드 시 macOS와 Xcode
- Git LFS

## 다른 컴퓨터에서 시작

```bash
git lfs install
git clone git@github.com:TestuyaSaito/Chamele-On.git
cd Chamele-On
git lfs pull
```

Unity Hub에서 **Add project from disk**를 선택한 뒤 이 저장소 폴더를 지정합니다. 처음 열 때 `Library`를 새로 만들기 때문에 에셋 임포트에 시간이 걸릴 수 있습니다.

플레이 장면:

`Assets/ChameleON/Scenes/GardenPrototype.unity`

## 에디터 조작

- 이동: `WASD` 또는 왼쪽 조이스틱. 절반 크기 캐릭터에 맞춘 탐색 속도는 1.60 m/s, 벽 부착 이동은 1.05 m/s입니다. 이동하면 캐릭터만 방향을 바꾸고 카메라는 자동으로 돌지 않습니다.
- 카메라: 탐색 모드에서 마우스 오른쪽 드래그 또는 오른쪽 빈 화면 스와이프. 카메라 회전은 항상 수동이며, 이 방식이 원작의 3인칭 조작과 같습니다.
- 페인트: `PAINT`를 누른 뒤 캐릭터에 마우스 왼쪽 드래그
- 페인트 카메라: 빈 공간에서 마우스 왼쪽 드래그
- 색상 추출: `EYEDROPPER`를 누르고 벽·바닥·사물을 탭하면 5×5 화면 표본의 중앙값에 가장 가까운 실제 픽셀을 브러시 색으로 사용합니다. 캐릭터 몸을 누르면 뒤쪽 사물을 잘못 고르지 않도록 선택을 거부합니다.
- 브러시: `BRUSH SIZE`의 `-`, `+`, 슬라이더로 기준 아틀라스 지름 6–68 px를 조절합니다. 가운데 원과 숫자가 현재 지름을 표시합니다.
- 되돌리기: `UNDO`로 최근 페인트 작업을 최대 8단계까지 복원합니다.
- 포즈: `POSE`
- 벽 부착: 벽 근처에서 `STICK`

## iOS 빌드

Unity 상단 메뉴에서 `ChameleON URP > Build iOS Xcode Project`를 실행합니다. 생성되는 `Builds/iOS-URP`는 Git에 포함되지 않습니다.

현재 앱 설정:

- Bundle ID: `com.chameleon.prototype`
- Apple Team ID: `PY57847KYX`
- App Store Connect 앱: `Chamele-ON Prototype`

Apple 인증서, `.p12`, 프로비저닝 프로파일, App Store Connect API 키는 보안상 이 저장소에 올리지 않습니다. 새 Mac에서는 Xcode에 같은 Apple ID로 로그인해 자동 서명을 다시 설정하세요.

Windows에서도 Unity 편집과 플레이 테스트는 가능하지만, iOS 빌드와 TestFlight 업로드는 macOS/Xcode에서 진행해야 합니다.

## 버전 관리 범위

Git에는 `Assets`, `Packages`, `ProjectSettings`와 프로젝트 문서만 저장합니다. `Library`, `Builds`, `Logs`, `UserSettings`는 각 컴퓨터에서 다시 생성됩니다. 대용량 텍스처와 3D 모델은 Git LFS로 관리합니다.
