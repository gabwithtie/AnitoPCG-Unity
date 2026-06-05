using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    public class AsyncOperationDispatcher
    {
        public bool IsRunning { get; private set; }
        public bool IsStopping() => false;
        public void RequestStop() { }
        public void ResetStagePromises() { }
        public void Pause() { }
        public void Unpause() { }
        public void MarkYieldPoint(Func<bool> predicate = null) { }
        public void IncrementProgress() { }
        public void FulfillStage(int stage, List<Shape> shapes) { }
        public void PublishState(List<Shape> shapes) { }
        public void MarkComplete() { }
        public void FulfilAllBlank() { }
        public void Fire(Action action) => Task.Run(action);
    }
}