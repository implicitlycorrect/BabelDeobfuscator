using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Cil;
using BabelDeobfuscator;
using System.Reflection;

string inputFile = args[0];
Assembly assembly = Assembly.UnsafeLoadFrom(inputFile);
ModuleDefinition module = ModuleDefinition.FromFile(inputFile);

// get typedef containing string decryption method
TypeDefinition? decryptionTypeDef = null;
foreach (TypeDefinition typeDef in module.GetAllTypes())
{
    // check if type does not have a static constructor with a methodbody that has instructions
    if (typeDef.GetStaticConstructor() is not { CilMethodBody.Instructions: { Count: > 1 } instructions })
        continue;

    bool staticCtorCallsGetExecutingAssembly = instructions.Any(x => x.OpCode == CilOpCodes.Call && x.Operand is IMethodDefOrRef { Name.Value: "GetExecutingAssembly" });
    if (!staticCtorCallsGetExecutingAssembly)
        continue;

    bool staticConstructorCallsGetManifestResourceStream = instructions.Any(x => x.OpCode == CilOpCodes.Callvirt && x.Operand is IMethodDefOrRef { Name.Value: "GetManifestResourceStream" });
    if (!staticConstructorCallsGetManifestResourceStream)
        continue;

    decryptionTypeDef = typeDef;
    break;
}

if (decryptionTypeDef is null)
    throw new MissingMemberException("failed to find typedef containing string decryption method");

int decryptionMethodMetadataToken = new MethodLocator(decryptionTypeDef)
    .MatchesSignature(isStatic: true, module.CorLibTypeFactory.String, new[] { module.CorLibTypeFactory.Int32 })
    .HasCilMethodBody()
    .HasInstruction(instruction => instruction.OpCode.Code is CilCode.Call && instruction.Operand is IMethodDefOrRef { Name.Value: "get_CurrentDomain" })
    .First()
    .MetadataToken
    .ToInt32();

MethodBase decryptionMethod = assembly.ManifestModule.ResolveMethod(decryptionMethodMetadataToken);

foreach (TypeDefinition typeDef in module.GetAllTypes())
{
    foreach (MethodDefinition methodDef in typeDef.Methods)
    {
        if (methodDef is not { CilMethodBody.Instructions: { Count: > 1 } instructions })
            continue;

        for (int i = 0; i < instructions.Count; i++)
        {
            // check if instruction is not call instruction
            if (instructions[i] is not { OpCode.Code: CilCode.Call, Operand: IMethodDefOrRef callee })
                continue;

            bool callsDecryptionMethood = callee.MetadataToken.ToInt32() == decryptionMethod.MetadataToken;
            if (!callsDecryptionMethood)
                continue;

            int decryptionKey = instructions[i - 1].GetLdcI4Constant();
            if (decryptionMethod.Invoke(null, new object?[] { decryptionKey }) is not string decrypted)
                continue;

            instructions[i].ReplaceWith(CilOpCodes.Ldstr, decrypted);
            instructions[i - 1].ReplaceWithNop();
        }
    }
}

string outputFile = inputFile.Insert(inputFile.Length - 4, "-deobfuscated");
module.Write(outputFile);