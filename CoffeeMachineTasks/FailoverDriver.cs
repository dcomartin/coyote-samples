﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Microsoft.Coyote.Random;
using Microsoft.Coyote.Tasks;

namespace Microsoft.Coyote.Samples.CoffeeMachineTasks
{
    /// <summary>
    /// This interface is designed to test how the CoffeeMachine handles "failover" or specifically,
    /// can it correctly "restart after failure" without getting into a bad state.  The CoffeeMachine
    /// will be randomly terminated.  The only thing the CoffeeMachine can depend on is
    /// the persistence of the state provided by the MockSensors.
    /// </summary>
    internal interface IFailoverDriver
    {
        Task RunTest();
    }

    /// <summary>
    /// This class implements the IFailoverDriver.
    /// </summary>
    internal class FailoverDriver : Loggable, IFailoverDriver
    {
        private readonly ISensors Sensors;
        private ICoffeeMachine CoffeeMachine;
        private bool IsInitialized;
        private bool RunForever;
        private int Iterations;
        private ControlledTimer HaltTimer;
        private readonly Generator RandomGenerator;

        public FailoverDriver(bool runForever, TextWriter log)
            : base(log, runForever)
        {
            this.RunForever = runForever;
            this.RandomGenerator = Generator.Create();
            this.Sensors = new MockSensors(this.Log, runForever);
        }

        public async Task RunTest()
        {
            bool halted = true;
            while (this.RunForever || this.Iterations <= 1)
            {
                this.WriteLine("#################################################################");

                // Create a new CoffeeMachine instance
                string error = null;
                if (halted)
                {
                    this.WriteLine("starting new CoffeeMachine iteration {0}.", this.Iterations);
                    this.IsInitialized = false;
                    this.CoffeeMachine = new CoffeeMachine(this.Log, this.RunForever);
                    halted = false;
                    this.IsInitialized = await this.CoffeeMachine.InitializeAsync(this.Sensors);
                    if (!this.IsInitialized)
                    {
                        error = "init failed";
                    }
                }

                if (error == null)
                {
                    // Setup a timer to randomly kill the coffee machine.   When the timer fires
                    // we will restart the coffee machine and this is testing that the machine can
                    // recover gracefully when that happens.
                    this.HaltTimer = new ControlledTimer("HaltTimer", TimeSpan.FromSeconds(this.RandomGenerator.NextInteger(7) + 1), new Action(this.OnStopTest));

                    // Request a coffee!
                    var shots = this.RandomGenerator.NextInteger(3) + 1;
                    error = await this.CoffeeMachine.MakeCoffeeAsync(shots);
                }

                if (string.Compare(error, "<halted>", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    // then OnStopTest did it's thing, so it is time to create new coffee machine.
                    this.WriteLine("CoffeeMachine is halted.");
                    halted = true;
                }
                else if (!string.IsNullOrEmpty(error))
                {
                    this.WriteLine("CoffeeMachine reported an error.");
                    this.RunForever = false; // no point trying to make more coffee.
                    this.Iterations = 10;
                }
                else
                {
                    // in this case we let the same CoffeeMachine continue on then.
                    this.WriteLine("CoffeeMachine completed the job.");
                }

                this.Iterations++;
            }

            // Shutdown the sensors because test is now complete.
            this.WriteLine("Test is complete, press ENTER to continue...");
            await this.Sensors.TerminateAsync();
        }

        internal void OnStopTest()
        {
            if (!this.IsInitialized)
            {
                // not ready!
                return;
            }

            if (this.HaltTimer != null)
            {
                this.HaltTimer.Stop();
                this.HaltTimer = null;
            }

            // Halt the CoffeeMachine.  HaltEvent is async and we must ensure the
            // CoffeeMachine is really halted before we create a new one because MockSensors
            // will get confused if two CoffeeMachines are running at the same time.
            // So we've implemented a terminate handshake here.  We send event to the CoffeeMachine
            // to terminate, and it sends back a HaltedEvent when it really has been halted.
            this.WriteLine("forcing termination of CoffeeMachine.");
            Task.Run(this.CoffeeMachine.TerminateAsync);
        }
    }
}
