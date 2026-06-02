// ================================================================
//  자전거 안전체험 시뮬레이터  ESP32-S3 펌웨어 v4.1
//  빛고을국민안전체험관 / FLUXION / 2026.05
//  v4.1: O·X 버튼 LED 제거
// ================================================================
//
//  [핀 배정]
//  GPIO1   Right Row4   PAS 센서 신호 (파란선, INPUT_PULLUP)
//  GPIO2   Right Row5   진동 모터 L (PWM)
//  GPIO42  Right Row6   진동 모터 R (PWM)
//  GPIO4   Left  Row4   브레이크 좌 (INPUT_PULLUP)
//  GPIO5   Left  Row5   브레이크 우 (INPUT_PULLUP)
//  GPIO6   Left  Row6   O 버튼 (INPUT_PULLUP)
//  GPIO7   Left  Row7   X 버튼 (INPUT_PULLUP)
//  GPIO11  Left  Row17  ICM-20948 SDA
//  GPIO12  Left  Row18  ICM-20948 SCL
//  GPIO48  내장          상태 RGB LED (WS2812)
//  3V3     Left  Row1   PAS + ICM-20948 + 진동모듈 VCC
//  GND     Left  Row22  공통 접지
//  USB-C   하단          PC 연결 (시리얼 + 전원)
//
//  [PAS 센서]  빨간→3V3  노란→GND  파란→GPIO1
//  [진동 모듈] VCC→3V3  GND→GND  IN_L→GPIO2  IN_R→GPIO42
//
//  [시리얼 ESP32→Unity] 115200bps / 50Hz / JSON
//  {"id":1,"rpm":80.0,"spd":20.0,"str":-5.2,"brkL":0,"brkR":0,"o":0,"x":0}
//
//  [시리얼 Unity→ESP32]
//  V0~5 진동 / C 조향캘리브레이션 / M 지자기캘리브레이션(5초 회전) / S0~3 RGB 상태
// ================================================================

#include <Wire.h>
#include <ICM_20948.h>
#include <MahonyAHRS.h>
#include "vibe_types.h"

ICM_20948_I2C icm;
Mahony        filter;

#define USE_RGB_LED  1
#if USE_RGB_LED
  #include <Adafruit_NeoPixel.h>
  Adafruit_NeoPixel rgb(1, 48, NEO_GRB + NEO_KHZ800);
  void setRGB(uint8_t r,uint8_t g,uint8_t b){
    rgb.setPixelColor(0,rgb.Color(r,g,b)); rgb.show(); }
#else
  void setRGB(uint8_t,uint8_t,uint8_t){}
#endif

// ── 핀 정의 ─────────────────────────────────────────────────────
#define PIN_PAS      1   // GPIO1   Right Row4  PAS 센서 (파란선)
#define PIN_VIBE_L   2   // GPIO2   Right Row5  진동 모터 L
#define PIN_VIBE_R  42   // GPIO42  Right Row6  진동 모터 R
#define PIN_BRK_L    4   // GPIO4   Left  Row4  브레이크 좌
#define PIN_BRK_R    5   // GPIO5   Left  Row5  브레이크 우
#define PIN_BTN_O    6   // GPIO6   Left  Row6  O 버튼
#define PIN_BTN_X    7   // GPIO7   Left  Row7  X 버튼
#define PIN_SDA     11   // GPIO11  Left  Row17 ICM-20948 SDA
#define PIN_SCL     12   // GPIO12  Left  Row18 ICM-20948 SCL
// GPIO9, GPIO10 미사용 (버튼 LED 제거)

// ── LEDC PWM (진동) ──────────────────────────────────────────────
#define VIBE_FREQ  1000
#define VIBE_BITS    8

// ── 시스템 상수 ─────────────────────────────────────────────────
#define STATION_ID       1
#define PAS_MAGNETS     12
#define RPM_MAX        300.0f   // F1: 스파이크 클램프 상한
#define CADENCE_TO_KPH  0.25f   // 60 RPM = 15 km/h
#define SERIAL_HZ         50
#define STEER_RANGE_DEG  45.0f
#define PAS_TIMEOUT   3000000UL
// F2: PaulStoffregen MahonyAHRS는 deg/s 입력 후 내부 변환
//     rad/s를 기대하는 포크 사용 시 (M_PI/180.0f) 로 변경
#define GYRO_SCALE     1.0f
// F9: 배열 길이 자동 계산
#define ARRAY_LEN(a)  (int)(sizeof(a)/sizeof((a)[0]))

// ── 진동 패턴 ────────────────────────────────────────────────────

const VibeStep PAT_DANGER[]  = {{255,700},{0,0}};
const VibeStep PAT_SUCCESS[] = {{220,150},{0,80},{220,150},{0,0}};
const VibeStep PAT_CORRECT[] = {{180,100},{0,0}};
const VibeStep PAT_WRONG[]   = {{255,450},{0,0}};
const VibeStep PAT_WALK[]    = {{200,100},{0,80},{200,100},{0,80},{200,100},{0,0}};
const VibeStep PAT_READY[]   = {{160,80},{0,60},{200,120},{0,0}};

static VibeStep  g_vSeq[10];
static int       g_vCount=0, g_vIdx=0;
static uint32_t  g_vEnd=0;

// ── 전역 변수 ────────────────────────────────────────────────────
volatile uint32_t g_lastPulseUs    = 0;
volatile uint32_t g_pulseIntervalUs= UINT32_MAX;
float             g_steerOffset    = 0.0f;
float             g_magOffX        = 0.0f;
float             g_magOffY        = 0.0f;
float             g_magOffZ        = 0.0f;
String            g_rxBuf          = "";
uint32_t          g_lastSendMs     = 0;

// ── 인터럽트 ─────────────────────────────────────────────────────
void IRAM_ATTR onPasPulse() {
    uint32_t now      = micros();
    g_pulseIntervalUs = now - g_lastPulseUs;
    g_lastPulseUs     = now;
}

// ── 진동 ─────────────────────────────────────────────────────────
void vibeSet(uint8_t s){ ledcWrite(PIN_VIBE_L,s); ledcWrite(PIN_VIBE_R,s); }
bool vibeIsPlaying(){ return g_vIdx < g_vCount; }
void vibeStop(){ g_vCount=g_vIdx=0; vibeSet(0); }

void playVibe(const VibeStep* p, int n){
    if(n<=0||n>10) return;
    memcpy(g_vSeq,p,(size_t)n*sizeof(VibeStep));
    g_vCount=n; g_vIdx=0;
    g_vEnd=millis()+p[0].ms;
    vibeSet(p[0].s);
}

void updateVibe(){
    if(g_vIdx>=g_vCount) return;
    if((uint32_t)(millis()-g_vEnd) >= 0x80000000UL) return; // F7: wrap-safe
    if(++g_vIdx>=g_vCount){ vibeSet(0); return; }
    vibeSet(g_vSeq[g_vIdx].s);
    g_vEnd=millis()+g_vSeq[g_vIdx].ms;
}

// ── ICM-20948 + Mahony ───────────────────────────────────────────
static inline float wrapYaw(float a, float ref){
    float d = fmodf(a - ref + 180.0f, 360.0f);
    if(d < 0.0f) d += 360.0f;
    return d - 180.0f;
}

float readSteering(){
    if(icm.dataReady()){
        icm.getAGMT();
        filter.update(icm.gyrX()*GYRO_SCALE, icm.gyrY()*GYRO_SCALE, icm.gyrZ()*GYRO_SCALE,
                      icm.accX(), icm.accY(), icm.accZ(),
                      icm.magX() - g_magOffX,
                      icm.magY() - g_magOffY,
                      icm.magZ() - g_magOffZ);
    }
    return constrain(wrapYaw(filter.getYaw(), g_steerOffset),
                     -STEER_RANGE_DEG, STEER_RANGE_DEG);
}

void calibrateSteering(){
    g_steerOffset = filter.getYaw();
    Serial.printf("{\"calibrated\":true,\"center\":%.1f}\n", g_steerOffset);
}

void calibrateMag(){
    vibeStop();                // F5: 블로킹 전 모터 즉시 OFF
    setRGB(0,0,200);           // F5: 파란색 = 지자기 캘리브레이션 중
    // 하드 아이언 캘리브레이션: 5초 동안 핸들을 좌우로 천천히 회전
    const uint32_t DUR = 5000;
    float xMn= 1e6f, xMx=-1e6f;
    float yMn= 1e6f, yMx=-1e6f;
    float zMn= 1e6f, zMx=-1e6f;
    Serial.println("{\"magcal\":\"start\",\"dur\":5}");
    uint32_t t0 = millis();
    while(millis() - t0 < DUR){
        if(icm.dataReady()){
            icm.getAGMT();
            float x=icm.magX(), y=icm.magY(), z=icm.magZ();
            if(x<xMn) xMn=x;  if(x>xMx) xMx=x;
            if(y<yMn) yMn=y;  if(y>yMx) yMx=y;
            if(z<zMn) zMn=z;  if(z>zMx) zMx=z;
        }
        delay(10);
    }
    g_magOffX = (xMx + xMn) * 0.5f;
    g_magOffY = (yMx + yMn) * 0.5f;
    g_magOffZ = (zMx + zMn) * 0.5f;
    Serial.printf("{\"magcal\":\"done\",\"ox\":%.2f,\"oy\":%.2f,\"oz\":%.2f}\n",
                  g_magOffX, g_magOffY, g_magOffZ);
    setRGB(0,200,0);           // F5: 복구
}

// ── 속도 ─────────────────────────────────────────────────────────
float calcCadenceRPM(){
    // F1: ISR과 공유하는 두 변수를 인터럽트 비활성화로 원자적 복사
    portDISABLE_INTERRUPTS();
    uint32_t interval = g_pulseIntervalUs;
    uint32_t last     = g_lastPulseUs;
    portENABLE_INTERRUPTS();
    if(micros()-last > PAS_TIMEOUT || interval == 0) return 0.0f;
    float rpm = 60.0f / ((float)interval / 1e6f / (float)PAS_MAGNETS);
    return constrain(rpm, 0.0f, RPM_MAX);   // F1: 스파이크 상한 클램프
}

// ── RGB 상태 ─────────────────────────────────────────────────────
enum RgbState{ RGB_IDLE,RGB_RUNNING,RGB_EVENT,RGB_QUIZ };
RgbState g_rgbState=RGB_IDLE;

void updateRGB(float spd, bool brakeAny){
    static uint32_t blinkMs=0; static bool blinkOn=false;
    uint32_t now=millis();
    if(now-blinkMs<400) return;
    blinkMs=now; blinkOn=!blinkOn;
    if(brakeAny)              { setRGB(255,80,0);  return; }
    if(g_rgbState==RGB_EVENT) { setRGB(255,0,0);   return; }
    if(g_rgbState==RGB_QUIZ)  { setRGB(blinkOn?100:0,0,blinkOn?100:0); return; }
    if(spd>1.0f)              { setRGB(0,200,0);   return; }
    setRGB(blinkOn?40:0,blinkOn?40:0,blinkOn?40:0);
}

// ── 시리얼 수신 ──────────────────────────────────────────────────
void handleCommand(const String& cmd){
    if(cmd.length()<1) return;
    char t=cmd.charAt(0);
    int  v=(cmd.length()>1)?cmd.substring(1).toInt():0;
    switch(t){
        case 'V':
            switch(v){
                case 0: vibeStop();              break;
                case 1: playVibe(PAT_DANGER,  ARRAY_LEN(PAT_DANGER));  break;
                case 2: playVibe(PAT_SUCCESS, ARRAY_LEN(PAT_SUCCESS)); break;
                case 3: playVibe(PAT_CORRECT, ARRAY_LEN(PAT_CORRECT)); break;
                case 4: playVibe(PAT_WRONG,   ARRAY_LEN(PAT_WRONG));   break;
                case 5: playVibe(PAT_WALK,    ARRAY_LEN(PAT_WALK));    break;
            } break;
        case 'C': calibrateSteering(); break;
        case 'M': calibrateMag();      break;
        case 'S': g_rgbState=(RgbState)constrain(v,0,3); break;
    }
}

void processSerial(){
    while(Serial.available()){
        char c=(char)Serial.read();
        if(c=='\n'||c=='\r'){
            g_rxBuf.trim();
            if(g_rxBuf.length()>0) handleCommand(g_rxBuf);
            g_rxBuf="";
        } else if(g_rxBuf.length()<16){ g_rxBuf+=c; }
    }
}

// ── JSON 전송 ─────────────────────────────────────────────────────
void sendJSON(float rpm,float spd,float str,
              bool bL,bool bR,bool o,bool x){
    Serial.printf(
        "{\"id\":%d,\"rpm\":%.1f,\"spd\":%.1f,\"str\":%.1f,"
        "\"brkL\":%d,\"brkR\":%d,\"o\":%d,\"x\":%d}\n",
        STATION_ID,rpm,spd,str,
        bL?1:0,bR?1:0,o?1:0,x?1:0);
}

// ── setup ─────────────────────────────────────────────────────────
void setup(){
    Serial.begin(115200);
    Wire.begin(PIN_SDA,PIN_SCL);

#if USE_RGB_LED
    rgb.begin(); rgb.setBrightness(40); setRGB(40,40,40);
#endif

    pinMode(PIN_PAS,   INPUT_PULLUP);
    attachInterrupt(digitalPinToInterrupt(PIN_PAS),onPasPulse,FALLING);
    pinMode(PIN_BRK_L, INPUT_PULLUP);
    pinMode(PIN_BRK_R, INPUT_PULLUP);
    pinMode(PIN_BTN_O, INPUT_PULLUP);
    pinMode(PIN_BTN_X, INPUT_PULLUP);

    ledcAttach(PIN_VIBE_L,VIBE_FREQ,VIBE_BITS);
    ledcAttach(PIN_VIBE_R,VIBE_FREQ,VIBE_BITS);
    vibeSet(0);

    // F3: AD0=HIGH(0x69) 먼저 시도, 실패 시 AD0=LOW(0x68) 재시도
    if(icm.begin(Wire, 1) != ICM_20948_Stat_Ok &&
       icm.begin(Wire, 0) != ICM_20948_Stat_Ok){
        Serial.println("{\"error\":\"ICM-20948 init failed\"}");
        // I2C 스캔으로 실제 연결된 주소 출력
        Serial.print("{\"i2c_scan\":[");
        bool first=true;
        for(uint8_t addr=1; addr<127; addr++){
            Wire.beginTransmission(addr);
            if(Wire.endTransmission()==0){
                if(!first) Serial.print(",");
                Serial.print(addr,HEX);
                first=false;
            }
        }
        Serial.println("]}");
        setRGB(255,0,0);
        while(true) delay(1000);
    }
    filter.begin(SERIAL_HZ);

    // F4: 필터 수렴 대기 — loop() 밖에서 직접 update() 호출 (2초 × 50Hz)
    for(int i=0; i<100; i++){
        if(icm.dataReady()){
            icm.getAGMT();
            filter.update(icm.gyrX()*GYRO_SCALE, icm.gyrY()*GYRO_SCALE, icm.gyrZ()*GYRO_SCALE,
                          icm.accX(), icm.accY(), icm.accZ(),
                          icm.magX()-g_magOffX, icm.magY()-g_magOffY, icm.magZ()-g_magOffZ);
        }
        delay(20);
    }
    calibrateSteering();
    playVibe(PAT_READY, ARRAY_LEN(PAT_READY));
    setRGB(0,200,0);
}

// ── loop ──────────────────────────────────────────────────────────
void loop(){
    uint32_t now=millis();
    updateVibe();
    processSerial();
    if(now-g_lastSendMs<(1000u/SERIAL_HZ)) return;
    g_lastSendMs=now;

    float rpm  = calcCadenceRPM();
    float spd  = rpm*CADENCE_TO_KPH;
    float str  = readSteering();
    bool  brkL = !digitalRead(PIN_BRK_L);
    bool  brkR = !digitalRead(PIN_BRK_R);
    bool  btnO = !digitalRead(PIN_BTN_O);
    bool  btnX = !digitalRead(PIN_BTN_X);

    if(!vibeIsPlaying()) vibeSet((brkL||brkR)?180:0);
    updateRGB(spd,brkL||brkR);
    sendJSON(rpm,spd,str,brkL,brkR,btnO,btnX);
}
