#include <AccelStepper.h>
#include <MultiStepper.h>



// PINOUT
#define EN 10
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


int velocidade_motor = 1000;
int aceleracao_motor = 100;
int max_x_step = 0;
int max_y_step = 0;
long x_array[19] = {0};
long y_array [17] = {0};
String cmd_input;
int chosen_position = 0;


AccelStepper motorX(1, 7, 4 );
AccelStepper motorY(2, 8, 5 );

void setup()
{
  Serial.begin(9600);
  pinMode(EN, OUTPUT);
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
  Serial.println("Type \"AT+GOTO(a number between 1 and 19)(X or Y)\"");
  
}

void loop()
{
  if (Serial.available() > 0)
  {
    cmd_input = Serial.readString();
    if (cmd_input.startsWith("AT+GOTO")){
      cmd_input.remove (0,7);
      if (cmd_input.endsWith("X")){
        if (cmd_input.toInt()>0 and cmd_input.toInt()<20){
           Serial.print("Going to position X: ");
           Serial.println(cmd_input.toInt());
           motorX.moveTo(x_array[cmd_input.toInt()-1]);
           digitalWrite(EN, LOW);
        }
        Serial.println ("Invalid position");
      }
      if (cmd_input.endsWith("Y")){
        if (cmd_input.toInt()>0 and cmd_input.toInt()<18){
           Serial.print("Going to position Y: ");
           Serial.println(cmd_input.toInt());
           motorX.moveTo(y_array[cmd_input.toInt()-1]);
           digitalWrite(EN, LOW);
        }
        Serial.println ("Invalid position");
      }
       
    }
    Serial.println ("Invalid command");
  } //end of command treatment
  motorX.run();

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

  digitalWrite(EN, HIGH);
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
