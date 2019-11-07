#include <AccelStepper.h>
#include <MultiStepper.h>



// PINOUT
#define EN_X 10
#define EN_Y 11
#define STEP 7
#define DIR 4
#define MIN_X_PIN 9
#define MIN_Y_PIN 8
#define MAX_X_PIN 6
#define MAX_Y_PIN 5
#define MAGNET_PIN 0

//CONFIG
#define STATES_DIF 40
#define MOTOR_SPEED 250
#define MOTOR_ACC 100

int max_x_step = 0;
int max_y_step = 0;
long x_array[19] = {0};
long y_array [17] = {0};
String cmd_input;
int chosen_position = 0;
int X1,X2,Y1,Y2;



AccelStepper motorX(1, 7, 4 );
AccelStepper motorY(2, 8, 5 );

void setup()
{
  Serial.begin(9600);
  pinMode(EN_X, OUTPUT);
  pinMode(EN_Y, OUTPUT);
  pinMode(MIN_X_PIN, INPUT);
  pinMode(MAX_X_PIN, INPUT);
  pinMode(MAGNET_PIN, OUTPUT);
  digitalWrite(MAGNET_PIN, LOW);
  motorX.setMaxSpeed(MOTOR_SPEED);
  motorX.setSpeed(MOTOR_SPEED);
  motorX.setAcceleration(MOTOR_ACC);
  motorY.setMaxSpeed(MOTOR_SPEED);
  motorY.setSpeed(MOTOR_SPEED);
  motorY.setAcceleration(MOTOR_ACC);
  calibration();
  /* ***************************
   * ********COMMANDS***********
   * ***************************
   * ||AT+OK                 |  Check communication (Will reply OK)
   * ||AT+GOTO(X1,Y1,X1,Y2)  |  Bring a piece from the position (X1,Y1) to (X2,Y2) where, either X1 and X2 are equal or Y1 and Y2 are. 
   *                            Where X1,X2 are numbers between 1 and 19 and Y1 and Y2 are between 1 and 17.
   * ||AT+CALIB              |  Calibrate the board
   * 
   */
  Serial.println("Type a command");
  
}

void loop()
{
  if (Serial.available() > 0)
  {
    cmd_input = Serial.readString();
    if (cmd_input.startsWith("AT+")){
      cmd_input.remove (0,3);
      if (cmd_input.startsWith("GOTO")){
        cmd_input.remove (0,5);
        X1 = cmd_input.toInt();
        if (X1>9){
          cmd_input.remove (0,2);  
        }
        else {
          cmd_input.remove (0,1);
        }
        Y1 = cmd_input.toInt();
        if (Y1>9){
          cmd_input.remove (0,2);  
        }
        else {
          cmd_input.remove (0,1);
        }
        X2 = cmd_input.toInt();
        if (X2>9){
          cmd_input.remove (0,2);  
        }
        else {
          cmd_input.remove (0,1);
        }
        Y2 = cmd_input.toInt();
        
        digitalWrite(EN_X, LOW);
        digitalWrite(EN_Y, LOW);
        while (motorX.run())
        while (motorY.run())
        
        digitalWrite(MAGNET_PIN,HIGH);
        motorX.moveTo(x_array[X2]);
        motorY.moveTo(y_array[Y2]);
        while (motorX.run())
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
    }
    else{
      Serial.println ("Invalid command");
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

}
void calibration() {
  Serial.println("Calibrating...");
  while (digitalRead(MIN_X_PIN) == LOW) {
    motorX.move(-10000);
    motorX.run();
  }
  motorX.setCurrentPosition(0);
  Serial.println("Minimum range reached");
  while (digitalRead(MAX_X_PIN) == LOW) {
    motorX.move(1000);
    motorX.run();
  }
  max_x_step = motorX.currentPosition();
  Serial.print("Maximum range reached: ");
  Serial.println(max_x_step);
  
  while (digitalRead(MIN_Y_PIN) == LOW) {
    motorY.move(-10000);
    motorY.run();
  }
  motorY.setCurrentPosition(0);
  Serial.println("Minimum range reached");
  while (digitalRead(MAX_Y_PIN) == LOW) {
    motorY.move(1000);
    motorY.run();
  }
  max_y_step = motorY.currentPosition();
  Serial.print("Maximum range reached: ");
  Serial.println(max_y_step);

  digitalWrite(EN_Y, HIGH);
  digitalWrite(EN_X, HIGH);
  x_array[9] = max_x_step/2 ;
  y_array[8] = max_y_step/2 ;
  x_array[0]= x_array[9] - 9 * STATES_DIF ;
  y_array[0]= y_array[8] - 9 * STATES_DIF ;
  for (int i=1; i<19;i++){
    x_array[i]=x_array[i-1]+STATES_DIF;
  }
  for (int i=1; i<17;i++){
    y_array[i]=y_array[i-1]+STATES_DIF;
  }
  
  return;
}
