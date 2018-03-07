﻿using System;
using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace InlineIL.Fody
{
    internal static class CecilExtensions
    {
        public static TypeDefinition ResolveRequiredType(this TypeReference typeRef)
        {
            try
            {
                var typeDef = typeRef.Resolve();

                if (typeDef == null)
                    throw new WeavingException($"Could not resolve type {typeRef.FullName}");

                return typeDef;
            }
            catch (Exception ex)
            {
                throw new WeavingException($"Could not resolve type {typeRef.FullName}: {ex.Message}");
            }
        }

        public static Instruction PrevSkipNops(this Instruction instruction)
        {
            instruction = instruction?.Previous;

            while (instruction != null && instruction.OpCode == OpCodes.Nop)
                instruction = instruction.Previous;

            return instruction;
        }

        public static Instruction NextSkipNops(this Instruction instruction)
        {
            instruction = instruction?.Next;

            while (instruction != null && instruction.OpCode == OpCodes.Nop)
                instruction = instruction.Next;

            return instruction;
        }

        public static void RemoveNopsAround(this ILProcessor il, Instruction instruction)
        {
            var instructions = il.Body.Instructions;
            var instructionIndex = instructions.IndexOf(instruction);
            if (instructionIndex < 0)
                return;

            while (instructionIndex < instructions.Count && instructions[instructionIndex].OpCode == OpCodes.Nop)
                instructions.RemoveAt(instructionIndex);

            while (--instructionIndex > 0 && instructions[instructionIndex].OpCode == OpCodes.Nop)
                instructions.RemoveAt(instructionIndex);
        }

        public static Instruction GetValueConsumingInstruction(this Instruction instruction)
        {
            var stackSize = 0;

            while (true)
            {
                stackSize += GetPushCount(instruction);

                instruction = instruction.Next;
                if (instruction == null)
                    throw new WeavingException("Unexpected end of method");

                stackSize -= GetPopCount(instruction);

                if (stackSize <= 0)
                    return instruction;
            }
        }

        public static Instruction[] GetArgumentPushInstructions(this Instruction instruction)
        {
            if (instruction.OpCode.FlowControl != FlowControl.Call)
                throw new WeavingException("Expected a call instruction");

            var method = (IMethodSignature)instruction.Operand;
            var argCount = GetArgCount(instruction.OpCode, method);

            if (argCount == 0)
                return Array.Empty<Instruction>();

            var result = new Instruction[argCount];
            var currentInstruction = instruction.Previous;

            for (var paramIndex = result.Length - 1; paramIndex >= 0; --paramIndex)
                result[paramIndex] = BackwardScanPush(ref currentInstruction);

            return result;
        }

        private static Instruction BackwardScanPush(ref Instruction currentInstruction)
        {
            Instruction result = null;
            var stackToConsume = 1;

            while (stackToConsume > 0)
            {
                var popCount = GetPopCount(currentInstruction);
                var pushCount = GetPushCount(currentInstruction);

                stackToConsume -= pushCount;

                if (stackToConsume == 0 && result == null)
                    result = currentInstruction;

                if (stackToConsume < 0)
                    throw new WeavingException("Unexpected stack behavior");

                stackToConsume += popCount;
                currentInstruction = currentInstruction.Previous;
            }

            if (result == null)
                throw new WeavingException("Could not locate call argument");

            return result;
        }

        private static int GetArgCount(OpCode opCode, IMethodSignature method)
        {
            var argCount = method.Parameters.Count;

            if (method.HasThis && !method.ExplicitThis && opCode.Code != Code.Newobj)
                ++argCount;

            if (opCode.Code == Code.Calli)
                ++argCount;

            return argCount;
        }

        private static int GetPopCount(Instruction instruction)
        {
            if (instruction.OpCode.FlowControl == FlowControl.Call)
                return GetArgCount(instruction.OpCode, (IMethodSignature)instruction.Operand);

            if (instruction.OpCode == OpCodes.Dup)
                return 0;

            switch (instruction.OpCode.StackBehaviourPop)
            {
                case StackBehaviour.Pop0:
                    return 0;

                case StackBehaviour.Popi:
                case StackBehaviour.Popref:
                case StackBehaviour.Pop1:
                    return 1;

                case StackBehaviour.Pop1_pop1:
                case StackBehaviour.Popi_pop1:
                case StackBehaviour.Popi_popi:
                case StackBehaviour.Popi_popi8:
                case StackBehaviour.Popi_popr4:
                case StackBehaviour.Popi_popr8:
                case StackBehaviour.Popref_pop1:
                case StackBehaviour.Popref_popi:
                    return 2;

                case StackBehaviour.Popi_popi_popi:
                case StackBehaviour.Popref_popi_popi:
                case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4:
                case StackBehaviour.Popref_popi_popr8:
                case StackBehaviour.Popref_popi_popref:
                    return 3;

                default:
                    throw new WeavingException("Could not locate method argument value");
            }
        }

        private static int GetPushCount(Instruction instruction)
        {
            if (instruction.OpCode.FlowControl == FlowControl.Call)
            {
                var method = (IMethodSignature)instruction.Operand;
                return method.ReturnType.MetadataType != MetadataType.Void || instruction.OpCode.Code == Code.Newobj ? 1 : 0;
            }

            if (instruction.OpCode == OpCodes.Dup)
                return 1;

            switch (instruction.OpCode.StackBehaviourPush)
            {
                case StackBehaviour.Push0:
                    return 0;

                case StackBehaviour.Push1:
                case StackBehaviour.Pushi:
                case StackBehaviour.Pushi8:
                case StackBehaviour.Pushr4:
                case StackBehaviour.Pushr8:
                case StackBehaviour.Pushref:
                    return 1;

                case StackBehaviour.Push1_push1:
                    return 2;

                default:
                    throw new WeavingException("Could not locate method argument value");
            }
        }

        public static bool IsStelem(this OpCode opCode)
        {
            switch (opCode.Code)
            {
                case Code.Stelem_Any:
                case Code.Stelem_I:
                case Code.Stelem_I1:
                case Code.Stelem_I2:
                case Code.Stelem_I4:
                case Code.Stelem_I8:
                case Code.Stelem_R4:
                case Code.Stelem_R8:
                case Code.Stelem_Ref:
                    return true;

                default:
                    return false;
            }
        }
    }
}