using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        private class Context
        {
            public bool IsCalculateDrillDistanceCalled
            {
                get;
                set;
            }

            public byte currentDrillStep { get; set; }
            public States currentState { get; set; }

            public byte currentExtruderStep { get; set; }

            public byte currentExtruderPositon { get; set; }

            public bool rotateDrillCalled { get; set; }


            public Context()
            {
                this.IsCalculateDrillDistanceCalled = false;
                this.currentDrillStep = 0;
                this.currentExtruderStep = 1;
                this.currentExtruderPositon = 1;
                this.rotateDrillCalled = false;
            }
        }

        // drills
        private List<IMyShipDrill> drills = new List<IMyShipDrill>();

        // rotors -> extruder top - bottom rotors, piston rotor
        private Dictionary<string, IMyMotorAdvancedStator> rotors = new Dictionary<string, IMyMotorAdvancedStator>();

        // drill pistons 
        private List<IMyPistonBase> drillPistons = new List<IMyPistonBase>();

        // extruder piston
        private IMyPistonBase extruderPiston;
        // welder 
        private IMyShipWelder welder;


        private Context context = new Context();

        private enum States
        {
            iddle = 0,
            preparing,
            rotating,
            calculateDrillDistance,
            drillExtending,
            drillRectracting,
            extruderExtending,
            extruderRectracting

        }

      

        public Program()
        {
            #region select blocks from game
            try { welder = GridTerminalSystem.GetBlockWithName("Extruder welder") as IMyShipWelder; }
            catch { throw new Exception("\"Extruder\" welder is not set"); }

            try
            {
                GridTerminalSystem.GetBlockGroupWithName("Drills").GetBlocksOfType(drills);
                GridTerminalSystem.GetBlockGroupWithName("Drills").GetBlocksOfType(drills);
            }
            catch { throw new Exception("\"Drills\" group is not set"); }

            try
            {
                GridTerminalSystem.GetBlockGroupWithName("Drill pistons").GetBlocksOfType(drillPistons);
                extruderPiston = GridTerminalSystem.GetBlockWithName("Extruder piston") as IMyPistonBase;
            }
            catch { throw new Exception("\"Drill pistons\" group is not set"); }


            try { rotors.Add("DrillRotor", GridTerminalSystem.GetBlockWithName("Drill rotor") as IMyMotorAdvancedStator); }
            catch { throw new Exception("\"Drill rotor\" not found! Set drill rotor to 'Drill rotor'"); }


            try { rotors.Add("TopRotor", GridTerminalSystem.GetBlockWithName("Top rotor") as IMyMotorAdvancedStator); }
            catch { throw new Exception("\"Top rotor\" is not set"); }


            try { rotors.Add("BottomRotor", GridTerminalSystem.GetBlockWithName("Bottom rotor") as IMyMotorAdvancedStator); }
            catch { throw new Exception("\"Bottom rotor\" rotor is not set"); }

            #endregion

            Dictionary<string, string> storage = parseStorage();

            //context.currentDrillStep = (byte)(storage.ContainsKey("currentDrillStep") ? byte.Parse(storage["currentDrillStep"]) : context.currentDrillStep);
            context.currentState = (States)(storage.ContainsKey("currentState") ? Enum.Parse(typeof(States),storage["currentState"]) : context.currentState);
            context.currentExtruderPositon = (byte)(storage.ContainsKey("currentExtruderPostion") ? byte.Parse(storage["currentExtruderPositon"]) : context.currentExtruderPositon);
            context.currentExtruderStep = (byte)(storage.ContainsKey("currentExtruderStep") ? byte.Parse(storage["currentExtruderStep"]) : context.currentExtruderStep);
            context.IsCalculateDrillDistanceCalled = storage.ContainsKey("IsCalculateDrillDistanceCalled ") && bool.Parse(storage["IsCalculateDrillDistanceCalled"]);
            // calculate max steps of drill
            maxDrillDistance = drillPistons.Count * 10f;
            maxStep = (byte)Math.Ceiling(maxDrillDistance / (drills.Count * 2.5f));


            extruderPiston.SetValueBool("ShareInertiaTensor", true);
            drillPistons.ForEach(piston=>piston.SetValueBool("ShareInertiaTensor", true)); 
            // safety reas
            rotors["TopRotor"].Attach();
            rotors["TopRotor"].RotorLock = true;
            rotors["TopRotor"].SetValueBool("ShareInertiaTensor", true);
           
            rotors["BottomRotor"].Attach();
            rotors["BottomRotor"].SetValueBool("ShareInertiaTensor", true);
            rotors["BottomRotor"].RotorLock = true;

            


            context.currentState = States.iddle;
            Runtime.UpdateFrequency = UpdateFrequency.Update100;

            
        }


        Dictionary<string, string> parseStorage()
        {
            Dictionary<string, string> states = new Dictionary<string, string>();
            /**
             * state is something like as string
             * key:value;anotherKey:value;diffirentKey:value
             * 
             * split it someting like
             * [ "key:value", "key:value"]
             * 
             * then split each item from :  like [ ["key", "value"] , ["key" , "value"] ] 
             * and create a Dictonory<string, string> = <key,value>
             */

            try
            {
                foreach (string state in Storage.Split(';'))
                {
                    string[] keyVal = state.Split(':');

                    states.Add(keyVal[0], keyVal[1]);
                }
            } catch
            {
                return new Dictionary<string, string>();
            }

            return states;



        }

        public void Save()
        {

            Storage = $"currentState:{context.currentState};" +
                $"IsCalculateDrillDistanceCalled:{context.IsCalculateDrillDistanceCalled};" +
                $"currentDrillStep:{context.currentDrillStep};" +
                $"currentExtruderPositon:{context.currentExtruderPositon};" +
                $"currentExtruderStep:{context.currentExtruderStep}";


        }


        /**
         * 
         * @iddle if not doing anything pistons, velders, grinders will be stopped for energy saving
        */


        void getStateFromArg(string arg)
        {

            switch (arg)
            {

                case "start":
                    context.currentState = States.preparing;

                    break;
                
                default:
                 
                    break;
                   
            }
        }
        public void Main(string argument, UpdateType updateSource)
        {

            getStateFromArg(argument);
            Echo(context.currentState.ToString());
            Echo("drill pos : " + context.currentDrillStep);
            Echo("current step : " + context.currentDrillStep);
            switch (context.currentState)
            {
                case States.iddle:
              
                    break;
                case States.rotating:
                    RotateDrill();
                    break;
                case States.drillExtending:
                
                    ExtendDrill();
                    break;
                case States.drillRectracting:
                    RectractDrill();
                    break;
                case States.extruderExtending:
                    
                    break;
                case States.extruderRectracting:
                
 
                    break;
                case States.calculateDrillDistance:
                    if (!context.IsCalculateDrillDistanceCalled) CalculateDrillDistance();
                    else context.currentState = States.drillExtending;
                    break;
                case States.preparing:
                    Prepare();
                    break;
                default:
                    break;
            }



        }


        #region globals for calculating drill distance per step
       
    
        byte maxStep;
        float maxDrillDistance;
        #endregion



        void CalculateDrillDistance()
        {

            if (context.IsCalculateDrillDistanceCalled)
            {
             
                return;
            }
            context.IsCalculateDrillDistanceCalled = true;
          

            if(context.currentDrillStep > maxStep)
            {

                // do nothing

                context.currentState = States.drillRectracting;
                return;
            }

     
            float currentDistance;

            if(maxStep > context.currentDrillStep)  currentDistance = context.currentDrillStep * (drills.Count * 2.5f);
            else currentDistance = maxDrillDistance;




            // distance for this step

            
            float distance = currentDistance;

            foreach (IMyPistonBase piston in drillPistons)
            {
                // get avalaible distance for this piston
                float availableDistance = 10f - piston.CurrentPosition;


               
                if (distance <= availableDistance)
                {
                    
                    piston.MaxLimit = piston.CurrentPosition + distance;
                    distance -= distance;

                }
                else {

                  
                    piston.MaxLimit = piston.CurrentPosition + availableDistance;
                    distance -=  availableDistance;

                }

            }





            context.currentDrillStep++;
        }


        #region prepare drills before start drilling
        // do not enable drills untill rectracting compleate for energy saving
        void enableDrills()
        {
            rotors["DrillRotor"].Displacement = 10f;
            drills.ForEach(drill => drill.Enabled = true);
        }


        
        void Prepare()
        {


           
            rotors["TopRotor"].Attach();
            rotors["BottomRotor"].Attach();

            // rectract all pistons default postion;
            drillPistons.ForEach(piston => {

                piston.MaxLimit = 0;
                piston.Velocity = .5f;
                piston.Retract();
            });

            rotors["DrillRotor"].Torque = 33600000f;
            rotors["DrillRotor"].TargetVelocityRad = .07f;


            if (drillPistons.TrueForAll(piston => piston.Status == PistonStatus.Extended))
            {
                enableDrills();
                context.currentState = States.preparing;
           
            }
            if(Math.Round((float)(180 / Math.PI) * rotors["DrillRotor"].Angle) != destinationAngle)
            {
                rotors["DrillRotor"].UpperLimitRad = 0;
                Echo("angle : " + Math.Round((180 / Math.PI) * rotors["DrillRotor"].Angle).ToString());
               
                context.currentState = States.preparing;
            }
            
            else
            {

                context.currentState = States.calculateDrillDistance;
            }

        }
        #endregion


        void RectractDrill()
        {

            drillPistons.ForEach(piston => {
                piston.Retract();
            });


            if(drillPistons.TrueForAll(piston => piston.Status == PistonStatus.Retracted))
            {
                context.currentState = States.extruderExtending;
            }
            



            
        }




        void ExtendExtruder()
        {

            Echo("extruder extendssss");
            extruderPiston.MaxLimit = (10f / 4) * context.currentExtruderStep;  

        }

        void ExtendDrill()
        {
            
            drillPistons.ForEach(piston => {

                piston.Velocity = 0.5f;
                piston.Extend();

                
            
            });



            if(drillPistons.TrueForAll(piston => piston.Status == PistonStatus.Extended))
                    context.currentState = States.rotating;

            else context.currentState = States.drillExtending;
        }


        void Cleanup()
        {

        }


        float destinationAngle = 0f;

        void RotateDrill()
        {

      
            IMyMotorAdvancedStator rotor = rotors["DrillRotor"];


            rotors["DrillRotor"].Torque = 33600000f;
            rotors["DrillRotor"].TargetVelocityRad = .07f;

            float currentAngle = (float)Math.Round((180 / Math.PI) * rotor.Angle);
           
            // check if rotor start from 360 degree
            if (!context.rotateDrillCalled)
            {
                if (currentAngle == 360)
                {

                    currentAngle = 0;
                    destinationAngle += 10f;
                }
                context.rotateDrillCalled = true;
            }
            


            Echo("currentAngle : " + currentAngle.ToString());
            Echo("TargetAngle : " + destinationAngle);

            if (currentAngle < destinationAngle) rotor.UpperLimitDeg = 360f;
            if (currentAngle >= destinationAngle) {


             
          
                if (destinationAngle >= 360f)
                {
                    destinationAngle = 0;
                    rotor.UpperLimitDeg = 0f;
                    context.rotateDrillCalled = false;
                    context.IsCalculateDrillDistanceCalled = false;
                    context.currentState = States.calculateDrillDistance;
                }

                destinationAngle += 10;


            }


        }



    }
}
