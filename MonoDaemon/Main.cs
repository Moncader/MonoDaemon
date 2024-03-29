/*
* Copyright (c) 2012 Jason Parrott
*
* Permission is hereby granted, free of charge, to any person obtaining * a copy of this software and associated documentation files (the "Software"), * to deal in the Software without restriction, including without limitation
* the rights to use, copy, modify, merge, publish, distribute, sublicense,
* and/or sell copies of the Software, and to permit persons to whom the * Software is furnished to do so, subject to the following conditions:
*
* The above copyright notice and this permission notice shall be included
* in all copies or substantial portions of the Software.
*
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. * IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
* DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
* TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
* SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

using System;
using Mono.Unix;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.IO;
using System.Collections.Generic;

namespace MonoDaemon
{
	class MainClass
	{
		public static void Main(string[] args)
		{
			if (args.Length != 2) {
				Console.WriteLine("Please provide a name for this daemon and a path to dll files to load. Aborting.");
				return;
			}
			
			string tName = args[0];
			string tDllPath = args[1];
			
			Console.WriteLine("Starting up MonoDaemon...");
			
			foreach (string tPath in Directory.GetFiles(tDllPath, "*.dll")) {
				Assembly.LoadFile(tPath);
				Console.WriteLine("Loaded assembly " + tPath);
			}
			
			Mono.Unix.Native.Syscall.unlink(tName);
			
			UnixEndPoint tEndpoint = new UnixEndPoint(tName);
			
			Socket tSocket = new Socket(tEndpoint.AddressFamily, SocketType.Stream, 0);
			
			Console.CancelKeyPress += (sender, e) => {
				if (tSocket.Connected) {
					tSocket.Close();
				}
			};
			
			tSocket.Bind(tEndpoint);
			tSocket.Listen(255);
			while (true) {
				Socket tConnection = tSocket.Accept();
				Console.WriteLine("Got request.");
				Thread tThread = new Thread(handleRequest);
				tThread.Start(tConnection);
			}
		}
		
		private static void handleRequest(object pData)
		{
			Console.WriteLine("Handling");
			Socket tConnection = (Socket)pData;
			byte[] tBytes = new byte[256];
			Buffer tBuffer = new Buffer(tBytes);
			while (true) {
				try {
					Request tRequest = new Request(tBuffer, tConnection);
					do {
						if (tBuffer.Index >= tBuffer.Length - 1) {
							tBuffer.Length = tConnection.Receive(tBytes, 256, SocketFlags.None);
							tBuffer.Index = 0;
						}
						if (tBuffer.Length == 0) {
							goto finish;
						}
						try {
							if (!tRequest.Parse()) {
								break;
							}
						} catch (OutOfBytesException) {
							//goto finish;
						}
					} while (true);
					
					if (tRequest.IsFinished) {
						break;
					}
				} catch (SocketException e) {
					Console.WriteLine(e);
					break;
				} catch (Exception e) {
					Console.WriteLine(e);
					break;
				}
				
				GC.Collect();
			}
		
			finish: {
				if (tConnection.Connected) {
					tConnection.Close();
				}
				Console.WriteLine("Shutting Down Thread");
			}
			
			GC.Collect();
		}
		
		private class Buffer
		{
			private byte[] mBytes;
			
			public int Index = 0;
			public int Length = 0;
			
			public Buffer(byte[] pBytes)
			{
				mBytes = pBytes;
			}
			
			public byte Get()
			{
				if (Index >= Length) {
					throw new OutOfBytesException();
				}
				return mBytes[Index++];
			}
		}
	
		private class OutOfBytesException : Exception
		{
			
		}
		
		private class Request
		{
			private const byte END = 0x00;
			private const byte STATE_STATIC_SET_PROPERTY = 0x01;
			private const byte STATE_STATIC_GET_PROPERTY = 0x02;
			private const byte STATE_STATIC_CALL = 0x03;
			private const byte STATE_NEW_CLASS = 0x04;
			private const byte STATE_DESTROY_CLASS = 0x05;
			private const byte STATE_GET_CLASS_PROPERTY = 0x06;
			private const byte STATE_SET_CLASS_PROPERTY = 0x07;
			private const byte STATE_CALL_CLASS_METHOD = 0x08;
			
			private const byte STATE_TYPE = 0x20;
			private const byte STATE_ARGUMENT = 0x40;
		
			
			private const byte TYPE_NULL = 0x01;
			private const byte TYPE_POINTER = 0x02;
			private const byte TYPE_EXCEPTION = 0x03;
			private const byte TYPE_STRING = 0x04;
			private const byte TYPE_INT = 0x05;
			private const byte TYPE_FLOAT = 0x06;
			private const byte TYPE_BOOL = 0x07;
			private const byte TYPE_VOID = 0x08;
			
			private Socket mConnection;
			private byte mState = END;
			private bool mIsFinished = false;
		
			public Request(Buffer pBuffer, Socket pConnection)
			{
				Buffer = pBuffer;
				mConnection = pConnection;
			}
			
			public Buffer Buffer;
			
			private byte get()
			{
				return Buffer.Get();
			}
			
			public bool IsFinished
			{
				get {
					return mIsFinished;
				}
			}
			
			private int toInt(byte[] pBytes)
			{
				if (BitConverter.IsLittleEndian) {
					Array.Reverse(pBytes);
				}
				return BitConverter.ToInt32(pBytes, 0);
			}
			
			private uint toUInt(byte[] pBytes)
			{
				if (BitConverter.IsLittleEndian) {
					Array.Reverse(pBytes);
				}
				return BitConverter.ToUInt32(pBytes, 0);
			}
			
			private float toFloat(byte[] pBytes)
			{
				if (BitConverter.IsLittleEndian) {
					Array.Reverse(pBytes);
				}
				return BitConverter.ToSingle(pBytes, 0);
			}
			
			private double toDouble(byte[] pBytes)
			{
				if (BitConverter.IsLittleEndian) {
					Array.Reverse(pBytes);
				}
				return BitConverter.ToDouble(pBytes, 0);
			}
			
			private string toASCIIString(byte[] pBytes)
			{
				return Encoding.ASCII.GetString(pBytes);
			}
			
			private string toUTF8String(byte[] pBytes)
			{
				return Encoding.UTF8.GetString(pBytes);
			}
			
			private byte[] fromInt(int pInt)
			{
				byte[] tBytes = BitConverter.GetBytes(pInt);
				if (BitConverter.IsLittleEndian) {
					Array.Reverse(tBytes);
				}
				return tBytes;
			}
			
			private byte[] fromUInt(uint pInt)
			{
				byte[] tBytes = BitConverter.GetBytes(pInt);
				if (BitConverter.IsLittleEndian) {
					Array.Reverse(tBytes);
				}
				return tBytes;
			}
			
			private byte[] fromFloat(float pFloat)
			{
				byte[] tBytes = BitConverter.GetBytes(pFloat);
				if (BitConverter.IsLittleEndian) {
					Array.Reverse(tBytes);
				}
				return tBytes;
			}
			
			private byte[] fromDouble(double pFloat)
			{
				byte[] tBytes = BitConverter.GetBytes(pFloat);
				if (BitConverter.IsLittleEndian) {
					Array.Reverse(tBytes);
				}
				return tBytes;
			}
			
			private byte[] fromBool(bool pBool)
			{
				return new byte[] { pBool ? (byte)0x01 : (byte)0x00 };
			}
			
			private byte[] fromASCIIString(string pString)
			{
				return Encoding.ASCII.GetBytes(pString + "\0x00");
			}
			
			private byte[] fromUTF8String(string pString)
			{
				byte[] tBytes = (new UTF8Encoding(false)).GetBytes(pString);
				Array.Resize<byte>(ref tBytes, tBytes.Length + 1);
				tBytes[tBytes.Length - 1] = (byte)0x00;
				return tBytes;
			}
		
			private byte[] fromObject(object pObject, byte pType)
			{
				switch (pType) {
					case TYPE_NULL:
						return new byte[] {};
					case TYPE_POINTER:
						return fromInt(pObject.GetHashCode());
					case TYPE_INT:
						return fromInt(System.Convert.ToInt32(pObject));
					case TYPE_FLOAT:
						return fromFloat(System.Convert.ToSingle(pObject));
					case TYPE_BOOL:
						return fromBool((bool)pObject);
					case TYPE_STRING:
						return fromUTF8String((string)pObject);
					default:
						return new byte[] {};
				}
			}
			
			private byte getType(object pObject)
			{
				if (pObject == null) {
					return TYPE_NULL;
				}
				if (pObject is string) {
					return TYPE_STRING;
				}
				Type tType = pObject.GetType();
				if (tType.IsPrimitive) {
					if (pObject is int) {
						return TYPE_INT;
					} else if (pObject is long) {
						return TYPE_INT;
					} else if (pObject is float) {
						return TYPE_FLOAT;
					} else if (pObject is double) {
						return TYPE_FLOAT;
					} else if (pObject is bool) {
						return TYPE_BOOL;
					} else {
						return TYPE_POINTER;
					}
				} else {
					return TYPE_POINTER;
				}
			}
			
			private byte[] merge(byte[][] pBytes)
			{
				int tSize = 0;
				for (int i = 0, il = pBytes.Length; i < il; i++) {
					tSize += pBytes[i].Length;
				}
				
				byte[] tBytes = new byte[tSize];
				int tIndex = 0;
				for (int i = 0, il = pBytes.Length; i < il; i++) {
					for (int j = 0, jl = pBytes[i].Length; j < jl; j++) {
						tBytes[tIndex++] = pBytes[i][j];
					}
				}
				
				return tBytes;
			}
		
			private class PersistanceTracker
			{
				public object Object;
				public int Count;
				
				public PersistanceTracker(object pObject, int pCount)
				{
					Object = pObject;
					Count = pCount;
				}
			}
			
			private void persistObject(object pObject, int pHash)
			{
				if (pObject == null) {
					return;
				}
				if (!pObject.GetType().IsPrimitive && !(pObject is string)) {
					if (mObjects.ContainsKey(pHash)) {
						mObjects[pHash].Count++;
					} else {
						mObjects[pHash] = new PersistanceTracker(pObject, 1);
					}
				}
			}
			
			private void persistObject(object pObject)
			{
				if (pObject == null) {
					return;
				}
				int tHash = pObject.GetHashCode();
				persistObject(pObject, tHash);
			}
			
			private void unPersistObject(int pHash)
			{
				if (!mObjects.ContainsKey(pHash)) {
					return;
				}
				if (--mObjects[pHash].Count == 0) {
					mObjects.Remove(pHash);
				}
			}
			
			private List<byte> mArgumentsBuffer = new List<byte>(64);
			private byte mArgumentType = END;
			
			private byte[] getArgumentBytes(int pSize)
			{
				while (mArgumentsBuffer.Count < pSize) {
					mArgumentsBuffer.Add(get());
				}
				
				byte[] tBytes = mArgumentsBuffer.ToArray();
				mArgumentsBuffer.Clear();
				return tBytes;
			}
			
			private object[] getArugments()
			{
				List<object> tArgs = new List<object>();
				
				while (true) {
					if (mArgumentType == END) mArgumentType = get();
					switch (mArgumentType) {
						case END:
							goto finish;
						case TYPE_NULL:
							tArgs.Add(null);
							break;
						case TYPE_POINTER:
							int tObjectPointer = toInt(getArgumentBytes(4));
							tArgs.Add(mObjects[tObjectPointer]);
							break;
						case TYPE_STRING:
							while (true) {
								byte tStringByte = get();
								if (tStringByte == END) {
									tArgs.Add(toUTF8String(mArgumentsBuffer.ToArray()));
									mArgumentsBuffer.Clear();
									break;
								} else {
									mArgumentsBuffer.Add(tStringByte);
								}
							}
							break;
						case TYPE_BOOL:
							if (get() == (byte)0x00) {
								tArgs.Add(false);
							} else {
								tArgs.Add(true);
							}
							break;
						case TYPE_INT:
							tArgs.Add(toInt(getArgumentBytes(4)));
							break;
						case TYPE_FLOAT:
							tArgs.Add(toFloat(getArgumentBytes(4)));
							break;
						default:
							throw new Exception("Invalid argument type: " + mArgumentType);
					}
					mArgumentType = END;
				}
				
				finish: {};
				
				return tArgs.ToArray();
			}
			
			/**
			 * Communication protocol is as follows.
			 * 
			 * Requests:
			 * 0x00 = end
			 *   When all ends have been popped, communication is over.
			 * 0x01 = static set to class property
			 * 0x02 = static get to class property
			 * 0x03 = static calling of class method
			 * 0x04 = new class
			 * 0x05 = destroy class
			 * 0x06 = get class property
			 * 0x07 = set class property
			 * 0x08 = call class method
			 * 0x70 = start argument
			 *   type{ends with 0x00}
			 *   length{4 bytes}
			 *   data{length}
			 * 
			 * Response:
			 * 0x00 = end
			 *   When all ends have been popped, communication is over.
			 * 0xFF = error
			 * 0x01 = type
			 *   type ()
			 */
			public bool Parse()
			{
				switch (mState) {
					case END:
						parseCommand();
						break;
				}
				return true;
			}
			
			private void parseCommand()
			{
				mState = get();
				parseCurrent();
			}
			
			private void parseCurrent() {
				switch (mState) {
					case END:
						mIsFinished = true;
						break;
					case STATE_STATIC_SET_PROPERTY:
						break;
					case STATE_STATIC_GET_PROPERTY:
						break;
					case STATE_STATIC_CALL:
						parseStaticCall();
						break;
					case STATE_NEW_CLASS:
						parseNewClass();
						break;
					case STATE_DESTROY_CLASS:
						parseDestroyClass();
						break;
					case STATE_GET_CLASS_PROPERTY:
						parseGetClassProperty();
						break;
					case STATE_SET_CLASS_PROPERTY:
						parseSetClassProperty();
						break;
					case STATE_CALL_CLASS_METHOD:
						parseCallClassMethod();
						break;
					default:
						break;
				}
			}
			
			
			private List<byte> mNewClassNameBytes = new List<byte>(64);
			private string mNewClassName = null;
			
			private Dictionary<int, PersistanceTracker> mObjects = new Dictionary<int, PersistanceTracker>(64);
			
			private void parseNewClass()
			{
				while (true) {
					if (mNewClassName == null) {
						byte tByte = get();
						if (tByte == END) {
							mNewClassName = toASCIIString(mNewClassNameBytes.ToArray());
							mNewClassNameBytes.Clear();
						} else {
							mNewClassNameBytes.Add(tByte);
						}
					} else {
						if (mIsGettingArgs) {
							mArgs = getArugments();
							mIsGettingArgs = false;
						} else {
							byte tByte = get();
							if (tByte == END) {
								mState = END;
								Type tType = Type.GetType(mNewClassName);
								object tObject = Activator.CreateInstance(tType, mArgs);
								int tHash = tObject.GetHashCode();
								mArgs = null;
								mNewClassName = null;
								persistObject(tObject, tHash);
								mConnection.Send(fromInt(tHash));
								return;
							} else if (tByte == STATE_ARGUMENT) {
								mIsGettingArgs = true;
							}
						}
					}
				}
			}
			
			private void parseDestroyClass()
			{
				while (true) {
					mClassParserBuffer.Add(get());
					if (mClassParserBuffer.Count == 4) {
						unPersistObject(toInt(mClassParserBuffer.ToArray()));
						mClassParserBuffer.Clear();
						mState = END;
						return;
					}
				}
			}
			
		
			private void parseStaticCall()
			{
				mState = END;
			}
			
			private List<byte> mClassParserBuffer = new List<byte>(64);
			private int mClassObject = -1;
			private string mCallClassMethodMethod = null;
			private object[] mArgs = null;
			private bool mIsGettingArgs = false;
			
			private Type[] getObjectTypes(object[] pObjects)
			{
				if (pObjects == null) {
					return new Type[0];
				}
				Type[] tTypes = new Type[pObjects.Length];
				for (int i = 0, il = pObjects.Length; i < il; i++) {
					tTypes[i] = pObjects[i].GetType();
				}
				return tTypes;
			}
			
			private void parseCallClassMethod()
			{
				while (true) {
					if (mClassObject == -1) {
						byte tByte = get();
						mClassParserBuffer.Add(tByte);
						if (mClassParserBuffer.Count == 4) {
							mClassObject = toInt(mClassParserBuffer.ToArray());
							mClassParserBuffer.Clear();
						}
					} else if (mCallClassMethodMethod == null) {
						byte tByte = get();
						if (tByte == END) {
							mCallClassMethodMethod = toASCIIString(mClassParserBuffer.ToArray());
							mClassParserBuffer.Clear();
						} else {
							mClassParserBuffer.Add(tByte);
						}
					} else {
						if (mIsGettingArgs) {
							mArgs = getArugments();
							mIsGettingArgs = false;
						} else {
							byte tByte = get();
							if (tByte == END) {
								object tObject = mObjects[mClassObject].Object;
								MethodInfo tMethodInfo = tObject.GetType().GetMethod(mCallClassMethodMethod, getObjectTypes(mArgs));
								object tReturn = tMethodInfo.Invoke(tObject, mArgs);
								byte[] tBytes;
								byte tType;
								if (tMethodInfo.ReturnType == typeof(void)) {
									tType = TYPE_VOID;
									tBytes = new byte[0];
								} else {
									tType = getType(tReturn);
									persistObject(tReturn);
									tBytes = fromObject(tReturn, tType);
								}
								mCallClassMethodMethod = null;
								mArgs = null;
								mClassObject = -1;
								mState = END;
								mConnection.Send(merge(new byte[][] {
									new byte[] { tType },
									tBytes
								}));
								return;
							} else if (tByte == STATE_ARGUMENT) {
								mIsGettingArgs = true;
							}
						}
					}
				}
			}
	
			private string mClassPropertyName = null;
			
			private void parseGetClassProperty()
			{
				while (true) {
					if (mClassObject == -1) {
						byte tByte = get();
						mClassParserBuffer.Add(tByte);
						if (mClassParserBuffer.Count == 4) {
							mClassObject = toInt(mClassParserBuffer.ToArray());
							mClassParserBuffer.Clear();
						}
					} else if (mClassPropertyName == null) {
						byte tByte = get();
						if (tByte == END) {
							mClassPropertyName = toASCIIString(mClassParserBuffer.ToArray());
							mClassParserBuffer.Clear();
														
							object tObject = mObjects[mClassObject].Object;
							MemberInfo[] tMemberInfoList = tObject.GetType().GetMember(mClassPropertyName);
							object tValue;
							
							foreach (MemberInfo tMemberInfo in tMemberInfoList) {
								switch (tMemberInfo.MemberType) {
									case MemberTypes.Field:
										FieldInfo tFieldInfo = (FieldInfo)tMemberInfo;
										tValue = tFieldInfo.GetValue(tObject);
										break;
									case MemberTypes.Property:
										PropertyInfo tPropertyInfo = (PropertyInfo)tMemberInfo;
										tValue = tPropertyInfo.GetValue(tObject, null);
										break;
									default:
										continue;
								}
								
								byte tType = getType(tValue);
								persistObject(tValue);
								byte[] tBytes = fromObject(tValue, tType);
								mClassPropertyName = null;
								mClassObject = -1;
								mState = END;
								mConnection.Send(merge(new byte[][] {
									new byte[] { tType },
									tBytes
								}));
								
								return;
							}
							
							throw new Exception("No property with the name: " + mClassPropertyName);
						} else {
							mClassParserBuffer.Add(tByte);
						}
					}
				}
			}
			
			private void parseSetClassProperty()
			{
				mState = END;
			}
		}
	}
}
