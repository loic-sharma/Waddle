using System.Collections.Generic;

namespace Waddle
{
    public class Stack
    {
        private readonly List<object> _stack = new List<object>();
        private int _stackIndex = 0;

        public object Pop()
        {
            return _stack[--_stackIndex];
        }

        public void Push(object value)
        {
            if (_stack.Count == _stackIndex)
            {
                _stack.Add(value);
                _stackIndex++;
            }
            else
            {
                _stack[_stackIndex++] = value;
            }
        }
    }
}
