#include <LiquidCrystal.h>

#include <AccelStepper.h>
#include <MultiStepper.h>

// PINOUT
#define EN_X 0
#define STEP_X 5
#define DIR_X 6

#define EN_Y 1
#define STEP_Y 3
#define DIR_Y 4

#define MIN_X_PIN 9
#define MIN_Y_PIN 8

#define MAGNET_PIN 10

#define LCD_RS 20
#define LCD_RW 21
#define LCD_EN 22
#define LCD_D4 23
#define LCD_D5 24
#define LCD_D6 25
#define LCD_D7 26

#define CONFIG_BUT 11

#define BUZ 20

#define POT A0

//CONFIG

#define STATES_DIF 40
#define MOTOR_SPEED 250
#define MOTOR_ACC 100
#define MAX_STEP_X 5000
#define MAX_STEP_Y 5000
#define SLEEP_TIME 20000 //in ms

#define VERBOSE

int max_x_step = 0;
int max_y_step = 0;
long x_array[8] = {0};
long y_array [8] = {0};
String cmd_input;
int X1,X2,Y1,Y2;
unsigned long configButPress;

AccelStepper motorX(1, STEP_X, DIR_X );
AccelStepper motorY(1, STEP_Y, DIR_Y );
LiquidCrystal lcd(LCD_RS, LCD_RW, LCD_EN, LCD_D4, LCD_D5, LCD_D6, LCD_D7);
  
  
  
  
  /* ***************************
   * ********COMMANDS***********
   * ***************************
   * ||AT+OK                 |  Check communication (Will reply OK)
   * ||AT+GOTO(X1,Y1,X2,Y2)  |  Bring a piece from the position (X1,Y1) to (X2,Y2) where, either X1 and X2 are equal or Y1 and Y2 are. 
   *                            Where X1,X2 are numbers between 1 and 19 and Y1 and Y2 are between 1 and 17.
   * ||AT+CALIB              |  Calibrate the board
   * ||AT+WIN
   * ||AT+LOSE
   * ||AT+ILEGALMOVE
   * ||AT+MISSPIEC
   * ****************************
   * 
   */
void setup()
{
  Serial.begin(9600);
  #ifdef VERBOSE
  Serial.println("Initializing LCD");
  #endif
 // lcd.begin(16,2);
  //lcd.print("Initializing...");

  #ifdef VERBOSE
  Serial.println("Initializing pins");
  #endif
  pinMode(EN_X, OUTPUT);
  pinMode(EN_Y, OUTPUT);
  pinMode(MIN_X_PIN, INPUT);
  pinMode(MIN_Y_PIN, INPUT);
  pinMode(MAGNET_PIN, OUTPUT);
  pinMode (CONFIG_BUT, INPUT);
  
  digitalWrite(MAGNET_PIN, LOW);
  #ifdef VERBOSE
  Serial.println("Setting motor X");
  #endif
  motorX.setMaxSpeed(MOTOR_SPEED);
  motorX.setSpeed(MOTOR_SPEED);
  motorX.setAcceleration(MOTOR_ACC);
  #ifdef VERBOSE
  Serial.println("Setting motor Y");
  #endif
  motorY.setMaxSpeed(MOTOR_SPEED);
  motorY.setSpeed(MOTOR_SPEED);
  motorY.setAcceleration(MOTOR_ACC);
  calibration();
  /*
  #ifdef VERBOSE
  Serial.println("Start config");
  #endif
  lcd.clear();
  lcd.print("---AutoChess---");
  lcd.setCursor(0,1);
  lcd.print("Press the button");
  #ifdef VERBOSE
  Serial.println("Waiting button press");
  #endif
  while (digitalRead(CONFIG_BUT)==LOW);
  lcd.clear();
  lcd.setCursor(0,0);
  lcd.print ("Pick your pieces");
  lcd.setCursor(0,1);
  lcd.print("W   or   B");
  delay(500);
  lcd.setCursor(0,1);
  lcd.cursor();
  #ifdef VERBOSE
  Serial.println("Wating selection");
  #endif
  while (digitalRead(CONFIG_BUT)==LOW){
    if (analogRead(POT)<512){
      lcd.setCursor(0,1);
    }
    else{
      lcd.setCursor(9,1);
    }
  }
  if (analogRead(POT)<512){
    Serial.println("AT+SELECT(W)");
    lcd.clear();
    lcd.setCursor(0,0);
    lcd.print ("White selected");
    delay (1000);
  }
  else{
    Serial.println("AT+SELECT(B)");
    lcd.clear();
    lcd.setCursor(0,0);
    lcd.print ("Black selected");
    delay(1000);
  }
  lcd.clear();
  lcd.setCursor(0,0);
  lcd.print ("Press the button");
  lcd.setCursor(0,1);
  lcd.print ("to start");
  while (digitalRead(CONFIG_BUT)==LOW);

   */
  Serial.println("AT+START");
  
}

void loop()
{
  if (CONFIG_BUT == HIGH){
    #ifdef VERBOSE
    Serial.println("Button pressed");
    #endif
    configButPress = millis();
  }
  if (Serial.available() > 0)
  {
    cmd_input = Serial.readString();
    if (cmd_input.startsWith("AT+")){
      cmd_input.remove (0,3);
      if (cmd_input.startsWith("GOTO")){
        cmd_input.remove (0,5);
        X1 = cmd_input.toInt();
        if (X1>9){
          cmd_input.remove (0,3);  
        }
        else {
          cmd_input.remove (0,2);
        }
        Y1 = cmd_input.toInt();
        if (Y1>9){
          cmd_input.remove (0,3);  
        }
        else {
          cmd_input.remove (0,2);
        }
        X2 = cmd_input.toInt();
        if (X2>9){
          cmd_input.remove (0,3);  
        }
        else {
          cmd_input.remove (0,2);
        }
        Y2 = cmd_input.toInt();
        
        digitalWrite(EN_X, LOW);
        digitalWrite(EN_Y, LOW);
        
        digitalWrite(MAGNET_PIN,LOW);
        #ifdef VERBOSE
        Serial.print("Starting in X=");
        Serial.print(X1);
        Serial.print("and Y=");
        Serial.println(Y1);
        #endif
        
        motorX.moveTo(x_array[X1]);
        motorY.moveTo(y_array[Y1]);
        #ifdef VERBOSE
        Serial.println("Moving motor X");
        #endif
        while (motorX.run())
        #ifdef VERBOSE
        Serial.println("Moving motor Y");
        #endif
        while (motorY.run())
        
        digitalWrite(MAGNET_PIN,HIGH);
        #ifdef VERBOSE
        Serial.print("Going to X=");
        Serial.print(X2);
        Serial.print("and Y=");
        Serial.println(Y2);
        #endif
        motorX.moveTo(x_array[X2]);
        motorY.moveTo(y_array[Y2]);
        #ifdef VERBOSE
        Serial.println("Moving motor X");
        #endif
        while (motorX.run())
        #ifdef VERBOSE
        Serial.println("Moving motor y");
        #endif
        while (motorY.run())
        digitalWrite(MAGNET_PIN,LOW);
        digitalWrite(EN_X, HIGH);
        digitalWrite(EN_Y, HIGH);
  
      }
      if (cmd_input.startsWith("OK")){
        Serial.println("OK");
      }
      if (cmd_input.startsWith("CALIB")){
      
        calibration();
      }
      if (cmd_input.startsWith("WWIN")){
        winSound();
      }
      if (cmd_input.startsWith("BWIN")){
        loseSound();
      }
      if (cmd_input.startsWith("ILLEGAL")){
        illegalMove();
      }
      if (cmd_input.startsWith("MISSPIEC")){
        missingPiece();
      }
    }
    else{
        #ifdef VERBOSE
        Serial.println("Invalid command");
        #endif
    }
  } //end of command treatment
  
  if (motorX.run()){
    digitalWrite(EN_X, LOW);
  }
  else{
    digitalWrite(EN_X, HIGH);
  }
  if (motorY.run()){
    digitalWrite(EN_Y, LOW);
  }
  else{
    digitalWrite(EN_Y, HIGH);
  }
  if ((millis() - configButPress)< SLEEP_TIME){
    
  }
  else{
    lcd.clear();
  }
  
}

void calibration() {
  #ifdef VERBOSE
  Serial.println("Calibrating...");
  #endif
  
  while (digitalRead(MIN_X_PIN) == LOW) {
    motorX.move(-10000);
    motorX.run();
  }
  motorX.setCurrentPosition(0);
  #ifdef VERBOSE
  Serial.println("Minimum range X reached");
  #endif
  

  max_x_step = MAX_STEP_X;

  while (digitalRead(MIN_Y_PIN) == LOW) {
    motorY.move(-10000);
    motorY.run();
  }
  motorY.setCurrentPosition(0);

  max_y_step = MAX_STEP_Y;


  digitalWrite(EN_Y, HIGH);
  digitalWrite(EN_X, HIGH);
  x_array[4] = max_x_step/2 ;
  y_array[4] = max_y_step/2 ;
  x_array[0]= x_array[8] - 9 * STATES_DIF ;
  y_array[0]= y_array[8] - 9 * STATES_DIF ;
  for (int i=1; i<8;i++){
    x_array[i]=x_array[i-1]+STATES_DIF;
  }
  for (int i=1; i<8;i++){
    y_array[i]=y_array[i-1]+STATES_DIF;
  }
  
  return;
}

void winSound(){
  #ifdef VERBOSE
  Serial.println("Win Sound");
  #endif
  tone(BUZ, 523.25,133);
  delay(133);
  tone(BUZ, 523.25,133);
  delay(133);
  tone(BUZ, 523.25,133);
  delay(133);
  tone(BUZ, 523.25,133);
  delay(133);
  tone(BUZ, 415.30,133);
  delay(133);
  tone(BUZ, 466.16,133);
  delay(133);
  tone(BUZ, 523.25,133);
  delay(133);
  tone(BUZ, 466.16,133);
  delay(133);
  tone(BUZ, 523.25,133);
  delay(133);

  return;
}
void loseSound(){
  #ifdef VERBOSE
  Serial.println("Lose Sound");
  #endif
  // G 391.995
  // A# 466.16
  // A 440
  // F# 369.99
  tone(BUZ, 391.995,133);
  delay(133);
  tone(BUZ, 391.995,133);
  delay(133);
  tone(BUZ, 391.995,133);
  delay(133);
  tone(BUZ, 466.16,133);
  delay(133);
  tone(BUZ, 440,133);
  delay(133);
  tone(BUZ, 440,133);
  delay(133);
  tone(BUZ, 391.995,133);
  delay(133);
  tone(BUZ, 391.995,133);
  delay(133);
  tone(BUZ, 369.99,133);
  delay(133);
  tone(BUZ, 391.995,133);
  delay(133);
  return;
}
void illegalMove(){
  #ifdef VERBOSE
  Serial.println("Ilegal Move detected");
  #endif
  lcd.setCursor(0, 1);
  lcd.print("Illegal Move!");
  tone(BUZ,440,250);
  tone(BUZ,440,250);
  lcd.setCursor(0,0);
  return;
}
void missingPiece(){
  #ifdef VERBOSE
  Serial.println("Missing piece detected");
  #endif
  lcd.setCursor(0, 1);
  lcd.print("Missing Pieces");
  tone(BUZ,550,250);
  tone(BUZ,550,250);
  tone(BUZ,550,250);
  lcd.setCursor(0,0);
  return;
}
