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

//CONFIG
#define STATES_DIF 40
#define MOTOR_SPEED 250
#define MOTOR_ACC 100


int velocidade_motor = 1000;
int aceleracao_motor = 100;
int max_x_step = 0;
long x_array[19] = {0};
long y_array [17] = {0};
String cmd_input;


AccelStepper motorX(1, 7, 4 );

void setup()
{
  Serial.begin(9600);
  pinMode(EN, OUTPUT);
  pinMode(MIN_X_PIN, INPUT);
  pinMode(MAX_X_PIN, INPUT);
  motorX.setMaxSpeed(MOTOR_SPEED);
  motorX.setSpeed(MOTOR_SPEED);
  motorX.setAcceleration(MOTOR_ACC);
  calibration();
  Serial.println("Type \"AT+GOTO(a number between 1 and 19)\" for the x motion");
  Serial.println("Type \"AT+GOTO(a number between 1 and 17)\" for the y motion");
  
}

void loop()
{
  if (Serial.available() > 0)
  {
    cmd_input = Serial.readString();
    if (cmd_input.startsWith("AT+GOTO")){
      cmd_input.remove (0,7);
      if (cmd_input.toInt()>0 and cmd_input.toInt()<20){
         Serial.print("Going to position: ");
         Serial.println(cmd_input.toInt());
         motorX.moveTo(x_array[cmd_input.toInt()-1]);
         digitalWrite(EN, LOW);
      }
      Serial.println ("Invalid position");
       
    }
    Serial.println ("Invalid command");
  } //end of command treatment
  motorX.run();
 // if (motorX.run()){
 //   digitalWrite(EN, LOW);
 // }
 // else {
 //   digitalWrite(EN, HIGH);
 // }
    /*{
      if (numero == '1')
      {
        Serial.println("Numero 1 recebido - Girando motor sentido horario.");
        digitalWrite(EN, LOW);
        sentido_horario = 1;
        sentido_antihorario = 0;
      }

      if (numero == '2')
      {
        Serial.println("Numero 2 recebido - Girando motor sentido anti-horario.");
        digitalWrite(EN, LOW);
        sentido_horario = 0;
        sentido_antihorario = 1;
      }

      if (numero == '3')
      {
        Serial.println("Numero 3 recebido - Parando motor...");
        sentido_horario = 0;
        sentido_antihorario = 0;
        motorX.moveTo(0);
        // digitalWrite(EN, HIGH);
      }
    }*/
  /*

  // Move o motor no sentido horario
  if (sentido_horario == 1)
  {
    motorX.moveTo(10000);
  }
  // Move o motor no sentido anti-horario
  if (sentido_antihorario == 1)
  {
    motorX.moveTo(-10000);
  }
  // Comando para acionar o motor no sentido especificado
  
  */
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

  digitalWrite(EN, HIGH);
  x_array[9] = max_x_step/2 ;
  x_array[0]= x_array[9] - 9 * STATES_DIF ;
  for (int i=1; i<19;i++){
    x_array[i]=x_array[i-1];
  }
  
  return;
}
