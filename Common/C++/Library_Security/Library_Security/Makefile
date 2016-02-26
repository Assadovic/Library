#!/bin/make
CC = gcc
CFLAGS = -shared -fPIC -Wall -O2 -pipe -D LINUX 
CXX = g++
CXXFLAGS = -shared -fPIC -Wall -O2 -pipe -D LINUX 
LDFLAGS = -lm

SRCS = Crc32_Castagnoli.cpp
OBJS := $(SRCS:.cpp=.o)

TARGET = Library_Security.so
BUILD = ./build

.PHONY: all clean
.SUFFIXES: .c .cpp .o

.c.o:
	$(CC) $(CFLAGS) -o $@ -c $^
.cpp.o:
	$(CXX) $(CXXFLAGS) -o $@ -c $^
.c:
	$(CC) $(CFLAGS) -o $@ $^ $(LDFLAGS)
.cpp:
	$(CXX) $(CXXFLAGS) -o $@ $^ $(LDFLAGS)

$(TARGET): $(OBJS)
	mkdir -p $(BUILD)
	$(CXX) $(CXXFLAGS) -o $(BUILD)/$(TARGET) $(OBJS)
	rm $(OBJS)

clean:
	rm -R $(BUILD)
	rm -R $(OBJS)
